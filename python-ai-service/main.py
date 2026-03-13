import json
from pathlib import Path

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from core.engine import AIEngine


def _load_settings() -> dict:
    path = Path(__file__).parent / "appsettings.json"
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8-sig"))


settings = _load_settings()
engine_cfg = settings.get("engine", {})
engine = AIEngine(
    gpu_workers=int(engine_cfg.get("gpu_workers", 1)),
    cpu_workers=int(engine_cfg.get("cpu_workers", 4)),
    batch_parallelism=int(engine_cfg.get("batch_parallelism", 6)),
    score_weights=engine_cfg.get("score_weights", {}),
    waste_thresholds=engine_cfg.get("waste_thresholds", {}),
)
app = FastAPI(title="PhotoSelector AI Service", version="3.2.0")


class AnalyzeRequest(BaseModel):
    image_path: str


class AnalyzeBatchRequest(BaseModel):
    image_paths: list[str] = Field(default_factory=list)


class FeedbackRequest(BaseModel):
    image_path: str
    manual_tag: str
    note: str = ""
    predicted_is_waste: bool = False
    predicted_style: str = "unknown"
    predicted_face_count: int = 0


@app.on_event("startup")
async def startup() -> None:
    engine.load_plugins()
    await engine.startup()


@app.on_event("shutdown")
async def shutdown() -> None:
    await engine.shutdown()


@app.get("/health")
async def health() -> dict:
    return {
        "status": "ok",
        "plugins": engine.plugin_names,
        "scheduler": engine.scheduler.queue_stats,
    }


@app.post("/analyze")
async def analyze(req: AnalyzeRequest) -> dict:
    try:
        return await engine.analyze(req.image_path)
    except FileNotFoundError as ex:
        raise HTTPException(status_code=404, detail=str(ex)) from ex
    except Exception as ex:
        raise HTTPException(status_code=500, detail=f"Analyze failed: {ex}") from ex


@app.post("/analyze/batch")
async def analyze_batch(req: AnalyzeBatchRequest) -> dict:
    if not req.image_paths:
        return {"items": []}

    try:
        items = await engine.analyze_many(req.image_paths)
        return {"items": items}
    except FileNotFoundError as ex:
        raise HTTPException(status_code=404, detail=str(ex)) from ex
    except Exception as ex:
        raise HTTPException(status_code=500, detail=f"Batch analyze failed: {ex}") from ex


@app.post("/feedback")
async def feedback(req: FeedbackRequest) -> dict:
    try:
        learning_state = engine.apply_feedback(
            manual_tag=req.manual_tag,
            predicted_is_waste=req.predicted_is_waste,
            predicted_face_count=req.predicted_face_count,
        )
        return {
            "status": "ok",
            "message": "feedback accepted",
            "learning_state": learning_state,
        }
    except Exception as ex:
        raise HTTPException(status_code=500, detail=f"Feedback failed: {ex}") from ex
