from __future__ import annotations

import cv2
import numpy as np

from core.plugin_base import BasePlugin, PluginOutput


class CurationPlugin(BasePlugin):
    name = "curation"
    requires_gpu = False
    priority = 20

    def analyze(self, image_path: str, image: np.ndarray | None = None) -> PluginOutput:
        if image is None:
            image = cv2.imread(image_path)
        if image is None:
            return PluginOutput(plugin_name=self.name, score=0.0, features={"status": "decode_failed"})

        hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
        mean_h = float(np.mean(hsv[:, :, 0]))
        mean_s = float(np.mean(hsv[:, :, 1]))
        mean_v = float(np.mean(hsv[:, :, 2]))

        if mean_s < 30:
            style_label = "low_saturation"
        elif 10 <= mean_h <= 42:
            style_label = "warm"
        elif 90 <= mean_h <= 140:
            style_label = "cool"
        else:
            style_label = "neutral"

        tone_label = "bright" if mean_v > 172 else "dark" if mean_v < 70 else "normal"
        style_score = 0.6 if style_label == "low_saturation" else 0.75 if style_label == "neutral" else 0.85
        tone_bonus = 0.08 if tone_label == "normal" else 0.0
        score = min(1.0, max(0.0, style_score + tone_bonus))
        color_label, dominant_colors = self._extract_dominant_colors(image)

        return PluginOutput(
            plugin_name=self.name,
            score=round(score, 4),
            features={
                "style_label": style_label,
                "tone_label": tone_label,
                "color_label": color_label,
                "dominant_colors": dominant_colors,
                "mean_saturation": round(mean_s, 2),
                "mean_brightness": round(mean_v, 2),
            },
        )

    @staticmethod
    def _extract_dominant_colors(image: np.ndarray) -> tuple[str, list[str]]:
        small = cv2.resize(image, (64, 64), interpolation=cv2.INTER_AREA)
        pixels = small.reshape(-1, 3).astype(np.float32)
        k = 5
        criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 24, 1.0)
        _, labels, centers = cv2.kmeans(pixels, k, None, criteria, 4, cv2.KMEANS_PP_CENTERS)

        counts = np.bincount(labels.flatten(), minlength=k)
        order = np.argsort(counts)[::-1]
        colors = []
        for idx in order:
            b, g, r = centers[idx]
            colors.append(f"#{int(r):02X}{int(g):02X}{int(b):02X}")

        top = centers[order[0]].astype(np.uint8)
        hsv = cv2.cvtColor(np.array([[top]]), cv2.COLOR_BGR2HSV)[0][0]
        hue = int(hsv[0])
        sat = int(hsv[1])
        val = int(hsv[2])

        if sat < 26:
            label = "gray"
        elif val < 35:
            label = "black"
        elif hue < 10 or hue >= 170:
            label = "red"
        elif hue < 22:
            label = "orange"
        elif hue < 33:
            label = "yellow"
        elif hue < 78:
            label = "green"
        elif hue < 95:
            label = "cyan"
        elif hue < 131:
            label = "blue"
        elif hue < 160:
            label = "purple"
        else:
            label = "magenta"

        return label, colors
