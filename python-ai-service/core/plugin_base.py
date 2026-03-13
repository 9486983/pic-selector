from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass
class PluginOutput:
    plugin_name: str
    score: float = 0.0
    objects: list[dict[str, Any]] = field(default_factory=list)
    features: dict[str, Any] = field(default_factory=dict)


class BasePlugin:
    name: str = "base"
    requires_gpu: bool = False
    priority: int = 100

    def analyze(self, image_path: str, image: np.ndarray | None = None) -> PluginOutput:
        raise NotImplementedError
import numpy as np
