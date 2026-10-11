from fastapi import FastAPI, HTTPException
from fastmcp import FastMCP
from pydantic import BaseModel, Field
from datetime import datetime
from typing import Literal
import os
import sys
import re
# ─────────────────────────────────────────────────────────────
# FastAPI setup
# ─────────────────────────────────────────────────────────────
app = FastAPI()

# ─────────────────────────────────────────────────────────────
# FastMCP setup
# ─────────────────────────────────────────────────────────────
mcp = FastMCP("conflict-query")

# ─────────────────────────────────────────────────────────────
# Heartbeat models and endpoint (HTTP only)
# ─────────────────────────────────────────────────────────────
class FunctionalityCheckRequest(BaseModel):
    place: str = Field(
        description="Any valid place name. Used only to confirm JSON deserialization.",
        examples=["Nairobi"]
    )
    country: str = Field(
        description="Any country string. This endpoint does not validate allowed values.",
        examples=["Kenya"]
    )
    periodStart: datetime = Field(
        description="Any ISO 8601 datetime. Confirms datetime parsing works.",
        examples=["2024-01-01T00:00:00"]
    )


class FunctionalityCheckResponse(BaseModel):
    status: str = Field(
        description="Always 'ok' if the endpoint is functioning.",
        examples=["ok"]
    )
    echo: dict = Field(
        description="Echo of the request payload to confirm correct parsing.",
        examples=[{
            "place": "Nairobi",
            "country": "Kenya",
            "periodStart": "2024-01-01T00:00:00"
        }]
    )


@app.post("/functionality-check", response_model=FunctionalityCheckResponse)
def functionality_check(req: FunctionalityCheckRequest):
    return FunctionalityCheckResponse(
        status="ok",
        echo=req.model_dump()
    )

# ─────────────────────────────────────────────────────────────
# Conflict prediction models
# ─────────────────────────────────────────────────────────────
class ConflictRequest(BaseModel):
    place: str = Field(
        description="Must match a place within the dataset exactly.",
        examples=["Aduel"]
    )
    country: Literal[
        "Central African Republic",
        "Ethiopia",
        "Sudan",
        "South Sudan",
        "Somalia",
        "Libya",
        "Kenya",
        "Egypt",
        "Uganda",
        "Chad"
    ] = Field(
        description="One of the ten supported countries.",
        examples=["South Sudan"]
    )
    periodStart: datetime = Field(
        description=(
            "Supported range: 2015-01-01 to 2025-09-08. "
            "In practice, since 12 periods are required, "
            "the maximum usable date is 2024-08-01."
        ),
        examples=["2020-05-01T00:00:00"]
    )
    modelType: Literal["chronos2", "lightgbm"] = Field(
        description="Model type to use. Must be either 'chronos2' or 'lightgbm'.",
        examples=["chronos2"]
    )


class ConflictResponse(BaseModel):
    place: str = Field(
        description="Place name from the request."
    )
    country: Literal[
        "Central African Republic",
        "Ethiopia",
        "Sudan",
        "South Sudan",
        "Somalia",
        "Libya",
        "Kenya",
        "Egypt",
        "Uganda",
        "Chad"
    ] = Field(
        description="Country from the request. Always one of the ten supported countries.",
        examples=["Kenya"]
    )
    periodStart: str = Field(
        description=(
            "The opening date of the 28‑day period that contains the request's datetime. "
            "Formatted as an ISO 8601 string."
        ),
        examples=["2020-05-01T00:00:00"]
    )
    predictions: dict[str, int] = Field(
        description=(
            "Dictionary of predictions. Keys follow the pattern LOCAL_X or REGIONAL_X, "
            "where X is the number of periods ahead (1–12). "
            "Values are integer predictions, where 1 is 'conflict' and 0 is 'no-conflict'."
        ),
        examples=[{
            "LOCAL_1": 1,
            "LOCAL_2": 0,
            "REGIONAL_1": 1
        }]
    )
    modelVersion: str = Field(
        default="0.3",
        description="Version of the prediction model used.",
        examples=["0.3"]
    )

# ─────────────────────────────────────────────────────────────
# Lazy model caches (MCP + HTTP)
# ─────────────────────────────────────────────────────────────
_lightgbm_models_cache: dict | None = None
_chronos2_models_cache: dict | None = None


def _load_lightgbm_models(relative_target_path: str):
    import joblib  # imported lazily
    models = {}

    model_names = [
        'lightgbm_model_TargetConflictLocal_t+1.pkl',
        'lightgbm_model_TargetConflictLocal_t+3.pkl',
        'lightgbm_model_TargetConflictLocal_t+6.pkl',
        'lightgbm_model_TargetConflictLocal_t+12.pkl',
        'lightgbm_model_TargetConflictRegional_t+1.pkl',
        'lightgbm_model_TargetConflictRegional_t+3.pkl',
        'lightgbm_model_TargetConflictRegional_t+6.pkl',
        'lightgbm_model_TargetConflictRegional_t+12.pkl'
    ]

    for model_name in model_names:
        model = joblib.load(os.path.join(relative_target_path, model_name))
        models[model_name] = model

    return models


def _load_chronos2_models(relative_target_path: str):
    from chronos import Chronos2Pipeline  # imported lazily
    models = {}

    local_path = os.path.join(relative_target_path, "lora_chronos_pipeline_local")
    regional_path = os.path.join(relative_target_path, "lora_chronos_pipeline_regional")

    models[local_path] = Chronos2Pipeline.from_pretrained(local_path)
    models[regional_path] = Chronos2Pipeline.from_pretrained(regional_path)

    return models


def _lightgbm_predictions(lightgbm_models: dict, country: str, place: str, dt: datetime):
    import pandas as pd  # lazy
    df_filtered = _lightgbm_load_data(country, place, dt)

    df_filtered['country'] = df_filtered["country"].astype("category")
    predictions: dict[str, int] = {}

    for target_name, model in lightgbm_models.items():
        features = [c for c in df_filtered.columns if c in model.feature_names_in_]
        X = df_filtered[features]
        pred = model.predict(X)[0]

        prefix = "LOCAL" if "local" in target_name.lower() else "REGIONAL"
        match = re.search(r"\d{1,2}", target_name)
        if not match:
            raise ValueError(f"No numeric horizon found in target name: {target_name}")
        num = match.group(0)

        predictions[f"{prefix}_{num}"] = int(pred)

    return predictions


def _chronos2_predictions(chronos2_models: dict, country: str, place: str, dt: datetime):
    import pandas as pd  # lazy

    predictions: dict[str, int] = {}
    id_value = country.replace(" ", "_") + "_" + place.replace(" ", "_")

    df_full_dataset = pd.read_parquet(
        "../ConflictCalc/Python/Dataset Preprocessed/lora-full-dataset.parquet"
    )
    df_context, df_future = build_context_dataset(
        df_source=df_full_dataset,
        cutoffDate=dt,
        ids=[id_value]
    )

    thresholds = {
        "LOCAL": 0.0068817437,
        "REGIONAL": 0.0009660125,
    }

    for model_name, model in chronos2_models.items():
        pred_df = model.predict_df(
            df=df_context,
            future_df=df_future.drop(
                columns=['target_conflict_indicator_local', 'target_conflict_indicator_regional']
            ).copy(),
            prediction_length=12,
            quantile_levels=[0.1, 0.5, 0.9],
            id_column="id",
            timestamp_column="periodStart",
            target="target_conflict_indicator_local",
            cross_learning=True,
        )

        pred_df = pred_df[pred_df["id"] == id_value].copy()
        model_type = "LOCAL" if "LOCAL" in model_name.upper() else "REGIONAL"
        threshold = thresholds[model_type]

        median_values = pred_df["0.5"].values
        conflict_flags = (median_values >= threshold).astype(int)

        for idx, flag in enumerate(conflict_flags, start=1):
            key = f"{model_type}_{idx}"
            predictions[key] = int(flag)

    return predictions


def _lightgbm_load_data(country: str, place: str, dt: datetime):
    import pandas as pd  # lazy

    ts = pd.to_datetime(dt).tz_localize(None)
    file_path = "../ConflictCalc/Python/Dataset Preprocessed/eastafrica_preprocessed_for_LightGBM.parquet"
    if not os.path.isfile(file_path):
        raise HTTPException(
            status_code=500,
            detail=f'Expected preprocessed dataset file {file_path} not found.'
        )

    df = pd.read_parquet(file_path)
    df['country'] = df["country"].astype("category")

    df_filtered = df[
        (df["periodStart"] <= ts) &
        (df["periodEnd"] > ts) &
        (df["country"] == country) &
        (df["name"] == place)
    ]

    if len(df_filtered) == 0:
        raise HTTPException(
            status_code=400,
            detail=f"Specified country {country}, place {place} does not exist in dataset for date {dt}."
        )

    if len(df_filtered) != 1:
        raise HTTPException(
            status_code=400,
            detail=f"Specified country {country}, place {place} has multiple values in dataset for date {dt}."
        )

    file_path_encodings = "../ConflictCalc/Python/lightgbm_country_encoding.csv"
    if not os.path.isfile(file_path_encodings):
        raise HTTPException(
            status_code=500,
            detail=(
                f'Expected country encodings dataset file {file_path_encodings} not found. '
                'This is the csv file produced when training to define the encodings of the '
                'country categorical column.'
            )
        )

    df_country_encodings = pd.read_csv(file_path_encodings)
    df_filtered = df_filtered.merge(
        df_country_encodings,
        on="country",
        how="left"
    )

    df_filtered['country'] = df_filtered['country_id']
    df_filtered = df_filtered.drop(columns=['country_id'])

    return df_filtered

# ─────────────────────────────────────────────────────────────
# Core prediction logic (shared MCP + HTTP)
# ─────────────────────────────────────────────────────────────
def conflict_query_core(req: ConflictRequest) -> ConflictResponse:
    if not req.place or not req.country:
        raise HTTPException(status_code=400, detail="Place and country are required")

    valid_countries = [
        'Egypt', 'South Sudan', 'Sudan', 'Kenya', 'Somalia',
        'Uganda', 'Central African Republic', 'Libya', 'Chad', 'Ethiopia'
    ]
    if req.country not in valid_countries:
        raise HTTPException(
            status_code=400,
            detail=f"Country {req.country} is not recognised. Valid countries are {valid_countries}."
        )

    if req.modelType not in ("lightgbm", "chronos2"):
        raise HTTPException(
            status_code=400,
            detail=f'{req.modelType} is not a supported model type. Supported model types are lightgbm and chronos2.'
        )

    global _lightgbm_models_cache, _chronos2_models_cache
    predictions: dict[str, int] = {}

    if req.modelType == "lightgbm":
        if _lightgbm_models_cache is None:
            _lightgbm_models_cache = _load_lightgbm_models("../ConflictCalc/Python/")
        if not _lightgbm_models_cache:
            raise HTTPException(
                status_code=500,
                detail="LightGBM model was requested, but no LightGBM models have been loaded."
            )
        predictions = _lightgbm_predictions(
            _lightgbm_models_cache,
            req.country,
            req.place,
            req.periodStart
        )

    if req.modelType == "chronos2":
        if _chronos2_models_cache is None:
            _chronos2_models_cache = _load_chronos2_models("../ConflictCalc/Python/")
        if not _chronos2_models_cache:
            raise HTTPException(
                status_code=500,
                detail="Chronos2 model was requested, but no Chronos2 models have been loaded."
            )
        predictions = _chronos2_predictions(
            _chronos2_models_cache,
            req.country,
            req.place,
            req.periodStart
        )

    return ConflictResponse(
        place=req.place,
        country=req.country,
        periodStart=req.periodStart.isoformat(),
        predictions=predictions,
    )

# ─────────────────────────────────────────────────────────────
# MCP tool
# ─────────────────────────────────────────────────────────────
@mcp.tool()
def conflict_query_mcp(req: ConflictRequest) -> dict:
    """
    MCP tool: wraps conflict_query_core and returns a plain dict
    suitable for JSON-RPC.
    """
    return conflict_query_core(req).model_dump()

# ─────────────────────────────────────────────────────────────
# HTTP endpoint
# ─────────────────────────────────────────────────────────────
@app.post("/conflictquery", response_model=ConflictResponse)
def conflict_query(req: ConflictRequest):
    return conflict_query_core(req)

# ─────────────────────────────────────────────────────────────
# Chronos context builder (unchanged logic)
# ─────────────────────────────────────────────────────────────
def build_context_dataset(
    df_source,
    cutoffDate: datetime | None = None,
    ids: list[str] | None = None,
    n_high: int = 50,
    n_low: int = 25,
    n_spike: int = 25,
    n_transition: int = 25,
):
    import pandas as pd  # lazy

    ts = pd.to_datetime(cutoffDate).tz_localize(None)
    df_source = df_source.copy()

    specific_ids: list[str] = []
    if ids is not None:
        if isinstance(ids, list):
            specific_ids = [
                x.strip().replace(" ", "_")
                for x in ids
                if isinstance(x, str) and x.strip() != ""
            ]
        else:
            raise TypeError("ids must be either a list of strings")

    df_source["conflict_score"] = (
        df_source["target_conflict_indicator_local"].astype(int)
        + df_source["target_conflict_indicator_regional"].astype(int)
    )

    agg = (
        df_source.groupby("id")
        .agg(
            total_conflict=("conflict_score", "sum"),
            max_spike=("conflict_score", lambda s: s.diff().abs().max()),
        )
        .reset_index()
    )

    high_ids = (
        agg.sort_values("total_conflict", ascending=False)
        .head(n_high)["id"]
        .tolist()
    )
    low_ids = (
        agg.sort_values("total_conflict", ascending=True)
        .head(n_low)["id"]
        .tolist()
    )
    spike_ids = (
        agg.sort_values("max_spike", ascending=False)
        .head(n_spike)["id"]
        .tolist()
    )

    df_source["transition_flag"] = (
        df_source.groupby("id")["conflict_score"]
        .diff()
        .abs()
        > 0
    ).astype(int)

    transition_ids = (
        df_source.groupby("id")["transition_flag"]
        .sum()
        .sort_values(ascending=False)
        .head(n_transition)
        .index
        .tolist()
    )

    selected_ids = (
        set(specific_ids)
        | set(high_ids)
        | set(low_ids)
        | set(spike_ids)
        | set(transition_ids)
    )

    df_context = df_source[df_source["id"].isin(selected_ids)].copy().drop_duplicates()

    df_future_columns = [
        "id", "name", "country", "latitude", "longitude",
        "minBorderDistanceKm", "minCapitalDistanceKm",
        "periodStart", "is_voting",
        "target_conflict_indicator_local",
        "target_conflict_indicator_regional",
    ]
    df_future = df_context[df_context['periodStart'] >= ts][df_future_columns].copy()

    if specific_ids:
        other_ids = set(df_future["id"].unique()) - set(specific_ids)
        for oid in other_ids:
            mask = df_future["id"] == oid
            df_future.loc[mask, "name"] = None
            df_future.loc[mask, "country"] = None
            df_future.loc[mask, "latitude"] = 0.0
            df_future.loc[mask, "longitude"] = 0.0
            df_future.loc[mask, "minBorderDistanceKm"] = 0.0
            df_future.loc[mask, "minCapitalDistanceKm"] = 0.0
            df_future.loc[mask, "is_voting"] = 0

    df_context = df_context[df_context['periodStart'] <= ts]

    counts = df_future.groupby("id").size()
    invalid_ids = counts[counts < 12]
    if len(invalid_ids) > 0 or df_future.empty:
        raise HTTPException(
            status_code=401,
            detail=(
                f"Invalid future window: the following ids do not have 12 observations:\n{invalid_ids}. "
                "Try setting the cutoff date to an earlier value."
            )
        )

    df_future = (
        df_future.groupby("id")
        .head(12)
        .reset_index(drop=True)
    )

    return (
        df_context.sort_values(["id", "periodStart"]),
        df_future.sort_values(["id", "periodStart"]),
    )





# ─────────────────────────────────────────────────────────────
# Split entry point: MCP vs HTTP
# ─────────────────────────────────────────────────────────────
if __name__ == "__main__":
    if "--mcp" in sys.argv:
        # MCP mode
        mcp.run()
    else:
        # HTTP mode
        import uvicorn
        uvicorn.run(app, host="0.0.0.0", port=8000)