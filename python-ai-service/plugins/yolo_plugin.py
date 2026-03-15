from __future__ import annotations

from pathlib import Path

import cv2
import numpy as np

from core.plugin_base import BasePlugin, PluginOutput


class YoloPlugin(BasePlugin):
    name = "yolo"
    requires_gpu = True
    priority = 10

    def __init__(self) -> None:
        self._model = None
        self._error = None
        self._face_cascade = None
        self._device = None
        try:
            from ultralytics import YOLO

            model_path = Path(__file__).resolve().parent.parent / "models" / "yolov8n.pt"
            if model_path.exists():
                self._model = YOLO(str(model_path))
            else:
                self._model = YOLO("yolov8n.pt")

            try:
                import torch

                if torch.cuda.is_available():
                    self._device = 0
            except Exception:
                self._device = None
        except Exception as ex:
            self._error = str(ex)

        try:
            cascade_path = Path(cv2.data.haarcascades) / "haarcascade_frontalface_default.xml"
            self._face_cascade = cv2.CascadeClassifier(str(cascade_path))
        except Exception:
            self._face_cascade = None

    def analyze(self, image_path: str, image=None) -> PluginOutput:
        if self._model is None:
            return PluginOutput(
                plugin_name=self.name,
                score=0.0,
                features={"status": "fallback", "reason": self._error or "YOLO unavailable"},
            )

        if self._device is None:
            results = self._model.predict(image_path, verbose=False, conf=0.35)
        else:
            results = self._model.predict(image_path, verbose=False, conf=0.35, device=self._device)
        result = results[0]
        objects: list[dict] = []
        person_count = 0
        image_area = 0.0
        if image is not None and hasattr(image, "shape"):
            image_area = float(image.shape[0] * image.shape[1])

        names = result.names
        boxes = result.boxes
        if boxes is not None:
            for box in boxes:
                cls_id = int(box.cls[0].item())
                conf = float(box.conf[0].item())
                xyxy = box.xyxy[0].tolist()
                label = str(names.get(cls_id, cls_id))
                width = max(0.0, float(xyxy[2]) - float(xyxy[0]))
                height = max(0.0, float(xyxy[3]) - float(xyxy[1]))
                box_area = width * height
                objects.append(
                    {
                        "label": label,
                        "confidence": round(conf, 4),
                        "x1": float(xyxy[0]),
                        "y1": float(xyxy[1]),
                        "x2": float(xyxy[2]),
                        "y2": float(xyxy[3]),
                    }
                )
                if (
                    label == "person"
                    and conf >= 0.45
                    and (image_area <= 0 or box_area / image_area >= 0.01)
                ):
                    person_count += 1

        # Fuse YOLO and face-based signals to reduce false positives/negatives.
        face_count = 0
        filtered_faces: list[tuple[int, int, int, int]] = []
        if image is not None and self._face_cascade is not None and not self._face_cascade.empty():
            gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
            faces = self._face_cascade.detectMultiScale(
                gray,
                scaleFactor=1.08,
                minNeighbors=7,
                minSize=(40, 40),
            )
            candidates: list[tuple[int, int, int, int]] = []
            for x, y, w, h in faces:
                if w <= 0 or h <= 0:
                    continue
                ratio = float(w) / float(h)
                if ratio < 0.68 or ratio > 1.45:
                    continue
                if image_area > 0 and (w * h) / image_area < 0.0015:
                    continue
                candidates.append((int(x), int(y), int(w), int(h)))

            candidates.sort(key=lambda b: b[2] * b[3], reverse=True)
            for candidate in candidates:
                if all(self._iou(candidate, kept) < 0.35 for kept in filtered_faces):
                    filtered_faces.append(candidate)

            face_count = len(filtered_faces)

        face_signature: list[float] = []
        if face_count > 0 and image is not None:
            largest = max(filtered_faces, key=lambda f: f[2] * f[3])
            x, y, w, h = [int(v) for v in largest]
            x = max(0, x)
            y = max(0, y)
            roi = image[y : y + h, x : x + w]
            if roi.size > 0:
                face_signature = self._compute_face_signature(roi)

        if person_count <= 0 and face_count > 0:
            # Conservative fallback: face detector is used only to recover missing single-portrait cases.
            person_count = 1

        score = 0.0 if not objects else min(1.0, sum(o["confidence"] for o in objects) / len(objects))
        return PluginOutput(
            plugin_name=self.name,
            score=round(score, 4),
            objects=objects,
            features={
                "count": len(objects),
                "person_count": person_count,
                "face_count": face_count,
                "face_signature": face_signature,
            },
        )

    @staticmethod
    def _iou(a: tuple[int, int, int, int], b: tuple[int, int, int, int]) -> float:
        ax1, ay1, aw, ah = a
        bx1, by1, bw, bh = b
        ax2, ay2 = ax1 + aw, ay1 + ah
        bx2, by2 = bx1 + bw, by1 + bh

        inter_w = max(0, min(ax2, bx2) - max(ax1, bx1))
        inter_h = max(0, min(ay2, by2) - max(ay1, by1))
        inter = float(inter_w * inter_h)
        if inter <= 0:
            return 0.0

        union = float(aw * ah + bw * bh - inter) + 1e-8
        return inter / union

    @staticmethod
    def _compute_face_signature(roi: np.ndarray) -> list[float]:
        try:
            gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
            gray = cv2.resize(gray, (64, 64), interpolation=cv2.INTER_AREA)
            gray = cv2.equalizeHist(gray)

            hog = cv2.HOGDescriptor(
                _winSize=(64, 64),
                _blockSize=(16, 16),
                _blockStride=(8, 8),
                _cellSize=(8, 8),
                _nbins=9,
            )
            hog_desc = hog.compute(gray).flatten()
            lbp_hist = YoloPlugin._lbp_hist(gray)
            vec = np.concatenate([hog_desc, lbp_hist]).astype(np.float32)
            norm = float(np.linalg.norm(vec)) + 1e-8
            vec = vec / norm
            return [round(float(v), 6) for v in vec.tolist()]
        except Exception:
            return []

    @staticmethod
    def _lbp_hist(gray: np.ndarray) -> np.ndarray:
        h, w = gray.shape
        if h < 3 or w < 3:
            return np.zeros(256, dtype=np.float32)

        center = gray[1:-1, 1:-1]
        lbp = np.zeros_like(center, dtype=np.uint8)
        neighbors = [
            gray[0:-2, 0:-2],
            gray[0:-2, 1:-1],
            gray[0:-2, 2:],
            gray[1:-1, 2:],
            gray[2:, 2:],
            gray[2:, 1:-1],
            gray[2:, 0:-2],
            gray[1:-1, 0:-2],
        ]

        for idx, n in enumerate(neighbors):
            lbp |= ((n >= center).astype(np.uint8) << (7 - idx))

        hist = np.bincount(lbp.flatten(), minlength=256).astype(np.float32)
        hist /= (hist.sum() + 1e-8)
        return hist
