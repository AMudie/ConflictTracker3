from fastapi import FastAPI, HTTPException #FastAPI allows this file to be run as an HTTP web server (with the Swagger UI at /docs)
from fastmcp import FastMCP #MCP server (In the same file!)
from pydantic import BaseModel, Field #class structure validation (BaseModel)
from datetime import datetime #timestamps
import joblib #for pickle files. 
import os #file system access
import pandas as pd #dataframes
import re #regular expressions
from chronos import Chronos2Pipeline #chronos 2 predictions
import sys 
from typing import Literal #tighter structure around the ConflictResponse class

#fastapi setup:
app = FastAPI() #create the FastAPI webserver

#fastmcp setup:
mcp = FastMCP("conflict-query") #create the MCP Json-RPC server

#split entry point, so it shoudl work for MCP And as an HTTP web server:
if __name__ == "__main__":
    import sys
    if "--mcp" in sys.argv:
        #mcp:
        mcp.run()
    else:
        #http webserver (REST API):
        import uvicorn
        uvicorn.run(app, host="0.0.0.0", port=8000)


#Global Variables:
#http path: http://localhost:8000/docs

#region "heartbeat" functionality

#heartbeats do not need to be available to the MCP server. 

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

#This is the actual endpoint for the heartbeat. Note the lack of MCP decoration!
@app.post("/functionality-check", response_model=FunctionalityCheckResponse)
def functionality_check(req: FunctionalityCheckRequest):
    """
    Lightweight endpoint to confirm the API is reachable and functioning.

    Returns:
    - status: always "ok"
    - echo: the exact request payload, confirming JSON parsing and datetime handling
    """
    return FunctionalityCheckResponse(
        status="ok",
        echo=req.model_dump()
    )

#End of region 

#region "Conflict Prediction"


##conflictquery endpoint designed for c# consumption:
#class ConflictRequest(BaseModel):
#    #class for the request, should deserialise json
#    place: str #Must match a place within the dataset. 
#    country: str #Ten countries are supported: ["Central African Republic", "Ethiopia", "Sudan", "South Sudan", "Somalia", "Libya", "Kenya", "Egypt", "Uganda", "Chad"]
#    periodStart: datetime #Officially the range 2015-01-01 to 2025-09-08 are supported, in practice since 12 periods are required then up to 2024-08-01 should be used as the maximum date. 
#    modelType: str #Two values are supported: ["chronos2" or "lightgbm"]

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



#class ConflictResponse(BaseModel):
#    #class for the response, should serialise and deserialise properly as json. 
#   place: str #place name from request
#   country: str #country from request, will be one of the ten supported countries ["Central African Republic","Ethiopia", "Sudan","South Sudan","Somalia", "Libya","Kenya","Egypt", "Uganda","Chad"] 
#   periodStart: str #will be the periodStart of the selected period (i.e. the opening point of the 28 day period the request's datetime is in. )
#   predictions: dict[str, int] #Dictionary. Keys will be LOCAL_X, or REGIONAL_X, depending on if the prediction is local or regional, and X is the number of periods from the supplied date, up to 12. 
#   modelVersion: str = "0.3" 

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
            "where X is the number of periods ahead (1–12)."
            "Values are integer predictions, where 1 is 'conflict' and 0 is 'no-conflict'"
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


##Loads the lightgbm models from disk, and returns as a dictionary, where the key is the model name, and the value is the model.  
def load_lightgbm_models(relative_target_path: str):
    #Target file example: C:\Users\andre\source\repos\ConflictTracker3\ConflictTracker\ConflictCalc\Python
    #This file: "C:\Users\andre\source\repos\ConflictTracker3\ConflictTracker\ConflictQuery\ConflictQuery.py"

   # relative_target_path = '../ConflictCalc/Python/'
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
        model = joblib.load(f"{relative_target_path}{model_name}")
        models[model_name] = model

    return models
    #End of load_lightgbm_models()

def load_chronos2_models(relative_target_path: str):
    models = {}

    model_names = [f'{relative_target_path}lora_chronos_pipeline_local', f'{relative_target_path}lora_chronos_pipeline_regional']
    for model_name in model_names:
        model = Chronos2Pipeline.from_pretrained(model_name)
        models[model_name] = model

    return models
    #end of load_chronos2_models

#This method needs to:
#1. Load the features from the presaved dataset (parquet) into a pandas dataframe of 1x item
#2. Drop extra features (e.g. targets for the other models)
#3. Use the supplied models to make a prediction
#4. Add the prediction to predictions variable
def lightgbm_predictions(lightgbm_models: dict, country: str, place: str, datetime: datetime):

    df_filtered = lightgbm_load_data(country, place, datetime)
    df_filtered['country'] = df_filtered["country"].astype("category")
    
    predictions = {}

    for target_name in lightgbm_models.keys():

        model = lightgbm_models[target_name]

        # Convert to 2D numpy array

        #These are the additional columns we remove when training the models, they exist in the parquet dataset so we can locate rows properly, but need shot of them for predicting.
        #additional_columns_to_remove = ["id", 'name',"periodStart", "periodEnd", "period_midpoint"]

        features = [c for c in df_filtered.columns if c in model.feature_names_in_] #construct a temporary df for this iteration: drops the targets we do not care about
        X = df_filtered[features]
      
        pred = model.predict(X)[0]   # extract scalar
        #predictions[target_name] = pred

        #LOCAL or REGIONAL
        prefix = "LOCAL" if "local" in target_name.lower() else "REGIONAL"

        #extract the first number (1–99)
        match = re.search(r"\d{1,2}", target_name)
        if match:
            num = match.group(0)
        else:
            raise ValueError(f"No numeric horizon found in target name: {target_name}")

        predictions[f"{prefix}_{num}"] = pred

    return predictions
    #End of lightgbm_predictions



#1. Construct context dataset
#2. Construct future dataset (pin down dt based on periodStart and periodEnd, then iterate to create mostly empty dataframe of 12 records, with the covariates filled in. )
#3. iterate over the models and create predictions. 
# return predictions.  
def chronos2_predictions(chronos2_models: dict, country: str, place: str, dt: datetime):
    predictions = {}

    id = country.replace(" ", "_") +  "_" + place.replace(" ", "_")

    #Load the full dataset and subsample out to the required values for the context and future windows. 
    df_full_dataset = pd.read_parquet("../ConflictCalc/Python/Dataset Preprocessed/lora-full-dataset.parquet")
    df_context, df_future = build_context_dataset(df_source = df_full_dataset, cutoffDate = dt, ids=[id] )

    thresholds = {}
    thresholds["LOCAL"] = 0.0068817437
    thresholds["REGIONAL"] = 0.0009660125

    for model_name in chronos2_models.keys():
        model = chronos2_models[model_name]

        pred_df = model.predict_df(
            df =df_context,
            future_df=df_future.drop(columns=['target_conflict_indicator_local', 'target_conflict_indicator_regional']).copy(),  #drop the target cols from the future df; we don't need them. 
            prediction_length=12,  # Number of steps to forecast 
            quantile_levels=[0.1, 0.5, 0.9],  # Quantiles for probabilistic forecast; 3x answers per test, model is 10%, 50% and 90% confident the value is below this answer. 
            id_column="id",  # Column identifying different time series
            timestamp_column="periodStart",  # Column with datetime information
            target="target_conflict_indicator_local",  # Column(s) with time series values to predict
            cross_learning=True,  # Enable cross-learning;
        ) 
        #disregard predictions to only the ID we care about
        pred_df = pred_df[pred_df["id"] == id].copy()

        #determine whether this model is local or regional
        model_type = "LOCAL" if "LOCAL" in model_name.upper() else "REGIONAL"
        threshold = thresholds[model_type]

        #extract the median quantile predictions (0.5)
        median_values = pred_df["0.5"].values  #length = 12

        #convert each prediction into a conflict flag using the threshold
        conflict_flags = (median_values >= threshold).astype(int)

        # Assign into the predictions dictionary
        for idx, flag in enumerate(conflict_flags, start=1):
            key = f"{model_type}_{idx}"
            predictions[key] = flag

    return predictions


#loads the dataset instance for a given country and place at period X. 
def lightgbm_load_data(country: str, place: str, dt: datetime):
    
    #Expected location "C:\Users\andre\source\repos\ConflictTracker3\ConflictTracker\ConflictCalc\Python\Dataset Preprocessed\eastafrica_preprocessed_for_LightGBM.parquet
    #This file: "C:\Users\andre\source\repos\ConflictTracker3\ConflictTracker\ConflictQuery\ConflictQuery.py"

    #ts = pd.to_datetime(dt) #pandas native datetime64 format. As we saved the parquet in. 
    ts = pd.to_datetime(dt).tz_localize(None)

    file_path = "../ConflictCalc/Python/Dataset Preprocessed/eastafrica_preprocessed_for_LightGBM.parquet"
    if os.path.isfile(file_path) == False:
        raise (f'Expected preprocessed dataset file {file_path} not found.')

    df = pd.read_parquet(file_path)

    df['country'] = df["country"].astype("category")


    df_filtered = df[
        (df["periodStart"] <= ts) &
        (df["periodEnd"] > ts) &
        (df["country"] == country) &
        (df["name"] == place)
    ]


    if len(df_filtered) == 0:
        raise (f"Specified country {country}, place {place} does not exist in dataset for date {datetime}.")

    if len(df_filtered) != 1:
        raise (f"Specified country {country}, place {place} has multiple values in dataset for date {datetime}.")

    #Fixing issues with transferring country encoding:

    df_country_encodings =[]
    file_path_encodings = "../ConflictCalc/Python/lightgbm_country_encoding.csv"
    if os.path.isfile(file_path_encodings) == False:
        raise (f'Expected country encodings dataset file {file_path_encodings} not found. This is the csv file produced when training to define the encodings of the country categorical column.')
    else:
        df_country_encodings = pd.read_csv("../ConflictCalc/Python/lightgbm_country_encoding.csv")

    #join the encoding table onto the filtered row
    df_filtered = df_filtered.merge(
        df_country_encodings,
        on="country",
        how="left"
    )

    #replace the country column with the numeric ID
    df_filtered['country'] = df_filtered['country_id']
    #drop the temporary column
    df_filtered = df_filtered.drop(columns=['country_id'])

    return df_filtered

#MCP endpoint for predicting:
@mcp.tool()
def conflict_query_mcp(req: ConflictRequest):
    return conflict_query_core(req).model_dump() 

#HTTP endpoint for predicting:
@app.post("/conflictquery", response_model=ConflictResponse)
def conflict_query(req: ConflictRequest):
    return conflict_query_core(req)

#this method does the actual work for predicting. 
def conflict_query_core(req: ConflictRequest):

    #print("API Python executable:", sys.executable)

    # Validate input (example)
    if not req.place or not req.country:
        raise HTTPException(status_code=400, detail="Place and country are required")

    valid_countries = ['Egypt', 'South Sudan', 'Sudan', 'Kenya', 'Somalia', 'Uganda', 'Central African Republic', 'Libya', 'Chad', 'Ethiopia']
    if req.country not in valid_countries:
        raise HTTPException(status_code=400, detail = f"Country {req.country} is not recognised. Valid countries are {valid_countries}.")

    modelType = req.modelType
    models = {}
    predictions = {}
    if (modelType != 'lightgbm' and modelType != 'chronos2'):
        raise HTTPException(status_code=400, detail = f'{req.modelType} is not a supported model type. Supported model types are lightgbm and chronos2.')

    #lightgbm:
    if (modelType == 'lightgbm'):
        relative_target_path_dur = '../ConflictCalc/Python/'
        models = load_lightgbm_models(relative_target_path_dur)

        predictions = lightgbm_predictions(models, req.country, req.place, req.periodStart)

        if len(models) == 0:
            raise HTTPException(status_code= 401, detail="LightGBM model was requested, but no LightGBM models have been loaded.")

    #chronos2
    if (req.modelType == 'chronos2'):
        relative_target_path_dur = '../ConflictCalc/Python/'
        models = load_chronos2_models(relative_target_path_dur)
        if len(models) == 0:
            raise HTTPException(status_code= 401, detail="Chronos2 model was requested, but no Chronos2 models have been loaded.")
        else:
            
            predictions = chronos2_predictions(models, req.country, req.place, req.periodStart)
            #Example: South Sudan/Aduel/South Sudan/26/09/2024

    return ConflictResponse(
        place=req.place,
        country=req.country,
        periodStart=req.periodStart.isoformat(),
        predictions = predictions
    )

#Oututs the context and future datasets, from the full dataset. Lifted directly from Chronos2ForTimeSeriesWithFixedData-TwoTargets.ipynb 
def build_context_dataset(
    df_source: pd.DataFrame,
    cutoffDate: datetime | None = None,
    ids: str | None = None,
    n_high: int = 50,
    n_low: int = 25,
    n_spike: int = 25,
    n_transition: int = 25,
):

    ts = pd.to_datetime(cutoffDate).tz_localize(None)

    df_source = df_source.copy()

    specific_ids = []
    
    if ids is not None:
        if isinstance(ids, list):
            # Normalize each ID in the list
            specific_ids = [
                x.strip().replace(" ", "_")
                for x in ids
                if isinstance(x, str) and x.strip() != ""
            ]
        else:
            raise TypeError("ids must be either a list of strings")

    
    # Use the new indicator columns as the conflict signal
    # Assuming they are 0/1 or boolean; cast to int to be safe.
    df_source["conflict_score"] = (
        df_source["target_conflict_indicator_local"].astype(int)
        + df_source["target_conflict_indicator_regional"].astype(int)
    )

    # Aggregate by id (place)
    agg = (
        df_source.groupby("id")
        .agg(
            total_conflict=("conflict_score", "sum"),
            max_spike=("conflict_score", lambda s: s.diff().abs().max()),
        )
        .reset_index()
    )

    # --- High conflict places -------------------------------------------------
    high_ids = (
        agg.sort_values("total_conflict", ascending=False)
        .head(n_high)["id"]
        .tolist()
    )

    # --- Low conflict places --------------------------------------------------
    low_ids = (
        agg.sort_values("total_conflict", ascending=True)
        .head(n_low)["id"]
        .tolist()
    )

    # --- Spike-based places ---------------------------------------------------
    spike_ids = (
        agg.sort_values("max_spike", ascending=False)
        .head(n_spike)["id"]
        .tolist()
    )

    #detect transitions: any change from previous timestep
    df_source["transition_flag"] = (
        df_source.groupby("id")["conflict_score"]
        .diff()
        .abs()
        > 0
    ).astype(int)
    
    #count transitions per id
    transition_ids = (
        df_source.groupby("id")["transition_flag"]
        .sum()
        .sort_values(ascending=False)
        .head(n_transition)
        .index
        .tolist()
    )
    
    # Combine unique ids
    selected_ids = (
        set(specific_ids)
        | set(high_ids)
        | set(low_ids)
        | set(spike_ids)
        | set(transition_ids)
    )

    #extract full windows for each selected id
    df_context = df_source[df_source["id"].isin(selected_ids)].copy().drop_duplicates()


    
    #dynamically build out the future set:
    df_future_columns =  ["id", "name",	"country",	"latitude",	"longitude",	"minBorderDistanceKm",	"minCapitalDistanceKm",	"periodStart", 'is_voting',  'target_conflict_indicator_local', 'target_conflict_indicator_regional'] #these are covariates we can know at the time of prediction. We also output the targets: we'll remove them when actually doing the training, but useful for evaluation.
    df_future = df_context[df_context['periodStart'] >= ts][df_future_columns].copy()


    #if we do specify a specific id(s), apply dummy values to the other ids in df_future:
    if specific_ids or len(ids) > 0:
        # IDs we do NOT care about
        other_ids = set(df_future["id"].unique()) - set(specific_ids)

        # Overwrite unwanted IDs with defaults
        for oid in other_ids:
            mask = df_future["id"] == oid

            # Overwrite all future covariates with safe defaults
            df_future.loc[mask, "name"] = None
            df_future.loc[mask, "country"] = None
            df_future.loc[mask, "latitude"] = 0.0
            df_future.loc[mask, "longitude"] = 0.0
            df_future.loc[mask, "minBorderDistanceKm"] = 0.0
            df_future.loc[mask, "minCapitalDistanceKm"] = 0.0
            df_future.loc[mask, "is_voting"] = 0 #error here where = False
    


    
    #now use cutoffDate to remove future records from the testing set:
    df_context = df_context[df_context['periodStart'] <= ts]   

    #validate: every id must have exactly 12 observations
    counts = df_future.groupby("id").size()
    invalid_ids = counts[counts < 12]
    if len(invalid_ids) > 0 or  df_future.empty:
        raise HTTPException(status_code = 401,detail=f"Invalid future window: the following ids do not have 12 observations:\n{invalid_ids}. Try setting the cutoff date to an earlier value.")

    #remove extra future records:
    df_future = (
        df_future.groupby("id")
        .head(12)
        .reset_index(drop=True)
    )
    
    return df_context.sort_values(["id", "periodStart"]), df_future.sort_values(["id", "periodStart"])
    #end of build_context_dataset

#End of region 