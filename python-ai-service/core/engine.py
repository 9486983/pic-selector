from __future__ import annotations

import asyncio
import importlib
import inspect
import json
import pkgutil
from pathlib import Path

import cv2
import numpy as np

from core.plugin_base import BasePlugin, PluginOutput
from core.scheduler import GpuCpuScheduler


class AIEngine:
    def __init__(
        self,
        gpu_workers: int = 1,
        cpu_workers: int = 4,
        batch_parallelism: int = 6,
        score_weights: dict | None = None,
        waste_thresholds: dict | None = None,
    ) -> None:
        self.plugins: list[BasePlugin] = []
        self.scheduler = GpuCpuScheduler(gpu_workers=gpu_workers, cpu_workers=cpu_workers)
        self.batch_parallelism = max(1, batch_parallelism)
        self.state_path = Path(__file__).resolve().parent.parent / "data" / "learning_state.json"
        self.state_path.parent.mkdir(parents=True, exist_ok=True)

        self.score_weights = score_weights or {
            "sharpness": 0.32,
            "exposure": 0.24,
            "object": 0.20,
            "person": 0.18,
            "style": 0.06,
        }
        self.waste_thresholds = waste_thresholds or {
            "hard_blur": 0.018,
            "soft_blur": 0.045,
            "bad_exposure": 0.12,
            "very_low_score": 0.10,
            "low_score_no_person": 0.15,
            "mid_low_score": 0.22,
        }
        self.feedback_stats: dict[str, int] = {
            "good_overruled_waste": 0,
            "waste_overruled_good": 0,
            "portrait_feedback": 0,
        }
        self.face_identities: dict[str, list[float]] = {}
        self.image_person_map: dict[str, str] = {}
        self.next_person_id = 1
        self._load_learning_state()
        self._ensure_weight_defaults()

    @property
    def plugin_names(self) -> list[str]:
        return [plugin.name for plugin in self.plugins]

    def load_plugins(self) -> None:
        self.plugins.clear()
        package = "plugins"
        package_path = Path(__file__).parent.parent / package

        for module_info in pkgutil.iter_modules([str(package_path)]):
            if module_info.name.startswith("_"):
                continue
            module = importlib.import_module(f"{package}.{module_info.name}")
            for _, obj in inspect.getmembers(module, inspect.isclass):
                if issubclass(obj, BasePlugin) and obj is not BasePlugin:
                    self.plugins.append(obj())

        self.plugins.sort(key=lambda p: p.priority)

    async def startup(self) -> None:
        await self.scheduler.start()

    async def shutdown(self) -> None:
        await self.scheduler.stop()
        self._save_learning_state()

    async def analyze(self, image_path: str) -> dict:
        path = Path(image_path)
        if not path.exists():
            raise FileNotFoundError(f"Image not found: {image_path}")

        image = cv2.imread(str(path))
        if image is None:
            raise ValueError(f"Unsupported image or decode failed: {image_path}")

        plugin_outputs: list[PluginOutput] = []
        for plugin in self.plugins:
            output = await self.scheduler.submit(plugin.requires_gpu, plugin.analyze, image_path, image)
            plugin_outputs.append(output)

        sharpness_score = self._estimate_sharpness(image)
        exposure_score = self._estimate_exposure(image)
        yolo_score = self._find_plugin_score(plugin_outputs, "yolo")
        style_score = self._find_plugin_score(plugin_outputs, "curation")
        person_count = self._extract_feature_int(plugin_outputs, "yolo", "person_count", default=0)
        face_signature = self._extract_feature_list(plugin_outputs, "yolo", "face_signature", default=[])
        style_label = self._extract_feature_str(plugin_outputs, "curation", "style_label", default="unknown")
        color_label = self._extract_feature_str(plugin_outputs, "curation", "color_label", default="unknown")
        dominant_colors = self._extract_feature_list(plugin_outputs, "curation", "dominant_colors", default=[])
        phash = self._perceptual_hash(image)
        person_label = self._resolve_person_label(face_signature, person_count, phash)

        person_score = min(1.0, person_count / 2.0)
        overall_score = (
            sharpness_score * self.score_weights.get("sharpness", 0.32)
            + exposure_score * self.score_weights.get("exposure", 0.24)
            + yolo_score * self.score_weights.get("object", 0.20)
            + person_score * self.score_weights.get("person", 0.18)
            + style_score * self.score_weights.get("style", 0.06)
        )
        overall_score = self._clamp(overall_score, 0.0, 1.0)

        is_waste, waste_reason = self._is_waste(
            sharpness_score=sharpness_score,
            exposure_score=exposure_score,
            person_count=person_count,
            overall_score=overall_score,
        )

        auto_class = self._auto_classify(person_count, style_label, sharpness_score, is_waste)

        return {
            "image_path": image_path,
            "overall_score": round(float(overall_score), 4),
            "sharpness_score": round(float(sharpness_score), 4),
            "exposure_score": round(float(exposure_score), 4),
            "eyes_closed": False,
            "is_duplicate": False,
            "face_count": person_count,
            "person_label": person_label,
            "style_label": style_label,
            "color_label": color_label,
            "dominant_colors": dominant_colors,
            "auto_class": auto_class,
            "is_waste": is_waste,
            "waste_reason": waste_reason,
            "plugins": [
                {
                    "plugin_name": out.plugin_name,
                    "score": out.score,
                    "objects": out.objects,
                    "features": out.features,
                }
                for out in plugin_outputs
            ],
        }

    async def analyze_many(self, image_paths: list[str]) -> list[dict]:
        semaphore = asyncio.Semaphore(self.batch_parallelism)

        async def _run(path: str) -> dict:
            async with semaphore:
                return await self.analyze(path)

        return await asyncio.gather(*[_run(path) for path in image_paths])

    def apply_feedback(
        self,
        manual_tag: str,
        predicted_is_waste: bool,
        predicted_face_count: int,
    ) -> dict:
        tag = manual_tag.lower().strip()
        if tag == "good" and predicted_is_waste:
            self.feedback_stats["good_overruled_waste"] += 1
            self.waste_thresholds["soft_blur"] = self._clamp(self.waste_thresholds["soft_blur"] - 0.004, 0.035, 0.09)
            self.waste_thresholds["low_score_no_person"] = self._clamp(
                self.waste_thresholds["low_score_no_person"] - 0.006, 0.12, 0.25
            )
        elif tag == "waste" and not predicted_is_waste:
            self.feedback_stats["waste_overruled_good"] += 1
            self.waste_thresholds["soft_blur"] = self._clamp(self.waste_thresholds["soft_blur"] + 0.004, 0.035, 0.09)
            self.waste_thresholds["low_score_no_person"] = self._clamp(
                self.waste_thresholds["low_score_no_person"] + 0.006, 0.12, 0.25
            )
        elif tag == "portrait":
            self.feedback_stats["portrait_feedback"] += 1
            if predicted_face_count == 0:
                self.score_weights["person"] = self._clamp(self.score_weights["person"] + 0.01, 0.10, 0.30)
                self.score_weights["object"] = self._clamp(self.score_weights["object"] - 0.01, 0.10, 0.28)

        self._save_learning_state()
        return {
            "score_weights": self.score_weights,
            "waste_thresholds": self.waste_thresholds,
            "feedback_stats": self.feedback_stats,
        }

    @staticmethod
    def _estimate_sharpness(image: np.ndarray) -> float:
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        score = cv2.Laplacian(gray, cv2.CV_64F).var()
        return min(max(score / 1200.0, 0.0), 1.0)

    @staticmethod
    def _estimate_exposure(image: np.ndarray) -> float:
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        brightness = float(np.mean(gray))
        diff = abs(brightness - 128.0)
        return max(0.0, 1.0 - diff / 128.0)

    @staticmethod
    def _find_plugin_score(outputs: list[PluginOutput], plugin_name: str) -> float:
        for out in outputs:
            if out.plugin_name == plugin_name:
                return float(out.score)
        return 0.0

    @staticmethod
    def _extract_feature_int(outputs: list[PluginOutput], plugin_name: str, key: str, default: int = 0) -> int:
        for out in outputs:
            if out.plugin_name == plugin_name:
                val = out.features.get(key, default)
                try:
                    return int(val)
                except Exception:
                    return default
        return default

    @staticmethod
    def _extract_feature_str(outputs: list[PluginOutput], plugin_name: str, key: str, default: str = "") -> str:
        for out in outputs:
            if out.plugin_name == plugin_name:
                val = out.features.get(key, default)
                return str(val)
        return default

    @staticmethod
    def _extract_feature_list(outputs: list[PluginOutput], plugin_name: str, key: str, default: list | None = None) -> list:
        fallback = default or []
        for out in outputs:
            if out.plugin_name == plugin_name:
                val = out.features.get(key, fallback)
                if isinstance(val, list):
                    normalized = []
                    for x in val:
                        try:
                            normalized.append(float(x))
                        except Exception:
                            normalized.append(str(x))
                    return normalized
                return fallback
        return fallback

    @staticmethod
    def _clamp(value: float, min_val: float, max_val: float) -> float:
        return max(min_val, min(max_val, value))

    def _is_waste(
        self,
        sharpness_score: float,
        exposure_score: float,
        person_count: int,
        overall_score: float,
    ) -> tuple[bool, str]:
        reasons: list[str] = []

        # Weighted risk avoids large false-positive waste outputs.
        risk = 0.0

        if sharpness_score < self.waste_thresholds["hard_blur"]:
            reasons.append("hard_blur")
            risk += 0.55
        elif sharpness_score < self.waste_thresholds["soft_blur"]:
            reasons.append("soft_blur")
            risk += 0.25

        if exposure_score < self.waste_thresholds["bad_exposure"]:
            reasons.append("bad_exposure")
            risk += 0.30

        if overall_score < self.waste_thresholds["very_low_score"]:
            reasons.append("very_low_score")
            risk += 0.50
        elif overall_score < self.waste_thresholds["mid_low_score"]:
            reasons.append("mid_low_score")
            risk += 0.20

        if person_count == 0 and overall_score < self.waste_thresholds["low_score_no_person"]:
            reasons.append("no_person_low_score")
            risk += 0.25

        # Portraits are less likely to be true waste unless clearly broken.
        if person_count > 0:
            risk -= 0.15

        if risk >= 0.80:
            return True, ",".join(reasons)
        return False, ",".join(reasons) if reasons else ""

    @staticmethod
    def _auto_classify(person_count: int, style_label: str, sharpness_score: float, is_waste: bool) -> str:
        if is_waste:
            return "waste"
        if person_count >= 2:
            return "portrait_group"
        if person_count == 1:
            return "portrait_single"
        if sharpness_score > 0.65:
            return f"detail_{style_label}"
        return f"scene_{style_label}"

    def _load_learning_state(self) -> None:
        if not self.state_path.exists():
            return
        try:
            state = json.loads(self.state_path.read_text(encoding="utf-8"))
            self.score_weights.update(state.get("score_weights", {}))
            self.waste_thresholds.update(state.get("waste_thresholds", {}))
            self.feedback_stats.update(state.get("feedback_stats", {}))
            self.face_identities.update(state.get("face_identities", {}))
            self.image_person_map.update(state.get("image_person_map", {}))
            self.next_person_id = int(state.get("next_person_id", self.next_person_id))
        except Exception:
            pass

    def _ensure_weight_defaults(self) -> None:
        defaults = {
            "sharpness": 0.32,
            "exposure": 0.24,
            "object": 0.20,
            "person": 0.18,
            "style": 0.06,
        }

        # Backward-compat for historical config key.
        if "face" in self.score_weights and "person" not in self.score_weights:
            self.score_weights["person"] = self.score_weights["face"]

        for key, val in defaults.items():
            self.score_weights.setdefault(key, val)

    def _save_learning_state(self) -> None:
        state = {
            "score_weights": self.score_weights,
            "waste_thresholds": self.waste_thresholds,
            "feedback_stats": self.feedback_stats,
            "face_identities": self.face_identities,
            "image_person_map": self.image_person_map,
            "next_person_id": self.next_person_id,
        }
        try:
            self.state_path.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception:
            pass

    def _resolve_person_label(self, face_signature: list, person_count: int, phash: str) -> str:
        if phash:
            if phash in self.image_person_map:
                return self.image_person_map[phash]
            near = self._find_phash_match(phash)
            if near is not None:
                return self.image_person_map[near]

        if person_count <= 0 or not face_signature:
            return "none"

        try:
            query = np.asarray(face_signature, dtype=np.float32)
        except Exception:
            return "none"

        best_label = ""
        best_sim = -1.0
        for label, vector in self.face_identities.items():
            try:
                base = np.asarray(vector, dtype=np.float32)
            except Exception:
                continue
            if base.size != query.size:
                continue

            denom = (np.linalg.norm(base) * np.linalg.norm(query)) + 1e-8
            sim = float(np.dot(base, query) / denom)
            if sim > best_sim:
                best_sim = sim
                best_label = label

        if best_label and best_sim >= 0.88:
            prev = np.asarray(self.face_identities[best_label], dtype=np.float32)
            merged = (prev * 0.75) + (query * 0.25)
            self.face_identities[best_label] = merged.tolist()
            if phash:
                self.image_person_map[phash] = best_label
            return best_label

        new_label = f"person_{self.next_person_id}"
        self.next_person_id += 1
        self.face_identities[new_label] = query.tolist()
        if phash:
            self.image_person_map[phash] = new_label
        return new_label

    def rename_person(self, old_label: str, new_label: str) -> dict:
        old = (old_label or "").strip()
        new = (new_label or "").strip()
        if not new:
            return {"status": "ignored", "reason": "empty_new_label"}
        if not old or old == new:
            return {"status": "noop", "label": new}

        if old in self.face_identities:
            vector = self.face_identities.pop(old)
            if new in self.face_identities:
                prev = np.asarray(self.face_identities[new], dtype=np.float32)
                incoming = np.asarray(vector, dtype=np.float32)
                merged = (prev * 0.6) + (incoming * 0.4)
                self.face_identities[new] = merged.tolist()
                status = "merged"
            else:
                self.face_identities[new] = vector
                status = "renamed"
            if self.image_person_map:
                for key, val in list(self.image_person_map.items()):
                    if val == old:
                        self.image_person_map[key] = new
            self._save_learning_state()
            return {"status": status, "label": new}

        return {"status": "not_found", "label": new}

    def assign_person(self, image_path: str, new_label: str) -> dict:
        label = (new_label or "").strip()
        if not label:
            return {"status": "ignored", "reason": "empty_label"}

        image = cv2.imread(str(image_path))
        if image is None:
            return {"status": "error", "reason": "decode_failed"}

        phash = self._perceptual_hash(image)
        if phash:
            self.image_person_map[phash] = label

        face_signature = []
        for plugin in self.plugins:
            if getattr(plugin, "name", "") == "yolo":
                try:
                    output = plugin.analyze(str(image_path), image)
                    face_signature = output.features.get("face_signature", [])
                except Exception:
                    face_signature = []
                break

        if face_signature:
            try:
                query = np.asarray(face_signature, dtype=np.float32)
                if label in self.face_identities:
                    prev = np.asarray(self.face_identities[label], dtype=np.float32)
                    merged = (prev * 0.7) + (query * 0.3)
                    self.face_identities[label] = merged.tolist()
                else:
                    self.face_identities[label] = query.tolist()
            except Exception:
                pass

        self._save_learning_state()
        return {"status": "ok", "label": label}

    @staticmethod
    def _perceptual_hash(image: np.ndarray) -> str:
        try:
            gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
            resized = cv2.resize(gray, (32, 32), interpolation=cv2.INTER_AREA)
            dct = cv2.dct(np.float32(resized))
            block = dct[:8, :8]
            med = float(np.median(block))
            bits = (block > med).flatten().tolist()
            value = 0
            for bit in bits:
                value = (value << 1) | (1 if bit else 0)
            return f"{value:016x}"
        except Exception:
            return ""

    def _find_phash_match(self, phash: str, max_distance: int = 6) -> str | None:
        try:
            target = int(phash, 16)
        except Exception:
            return None

        best_key = None
        best_dist = max_distance + 1
        for key in self.image_person_map.keys():
            try:
                val = int(key, 16)
            except Exception:
                continue
            dist = (target ^ val).bit_count()
            if dist < best_dist:
                best_dist = dist
                best_key = key

        return best_key if best_key is not None and best_dist <= max_distance else None
