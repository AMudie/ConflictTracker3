from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from datetime import datetime

app = FastAPI()


#Original endpoint to confirm functionality
class Query(BaseModel):
    place: str
    country: str
    periodStart: datetime

@app.post("/predict_basic")
def predict_basic(q: Query):
    # Your original logic stays here
    return {
        "place": q.place,
        "country": q.country,
        "periodStart": q.periodStart.isoformat(),
        "prediction": "example"
    }



#conflictquery enddpoing designed for c# consumption:
class ConflictRequest(BaseModel):
    #class for the request, should deserialise json
    place: str
    country: str
    periodStart: datetime

class ConflictResponse(BaseModel):
    #class for the response, should serialise and deserialise properly as json. 
    place: str
    country: str
    periodStart: str
    riskScore: float
    modelVersion: str = "1.0"

@app.post("/conflictquery", response_model=ConflictResponse)
def conflict_query(req: ConflictRequest):
    # Validate input (example)
    if not req.place or not req.country:
        raise HTTPException(status_code=400, detail="Place and country are required")

    # TODO: Insert your LightGBM model logic here
    # For now, return a dummy score
    risk_score = 0.42

    return ConflictResponse(
        place=req.place,
        country=req.country,
        periodStart=req.periodStart.isoformat(),
        riskScore=risk_score
    )
