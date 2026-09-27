from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from datetime import datetime
import joblib #for pickle files. 
import sklearn
import os #file system access
import pandas as pd
import re #regular expressions
from chronos import Chronos2Pipeline
import sys


app = FastAPI()

#Global Variables:
#http path: http://localhost:8000/docs

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



#conflictquery enddpoint designed for c# consumption:
class ConflictRequest(BaseModel):
    #class for the request, should deserialise json
    place: str
    country: str
    periodStart: datetime
    modelType: str #lightgbm or chronos2. 

class ConflictResponse(BaseModel):
    #class for the response, should serialise and deserialise properly as json. 
    place: str
    country: str
    periodStart: str
    predictions: dict[str, int]
    modelVersion: str = "0.2"

##Loads the lightgbm models from disk, and returns as a dictionary, where the key is the model name, and the value is the model.  
def load_lightGBM_models(relative_target_path: str):
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
    #End of load_lightGBM_models()

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
    thresholds["local"] = 0.0068817437
    thresholds["regional"] = 0.0009660125

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

@app.post("/conflictquery", response_model=ConflictResponse)
def conflict_query(req: ConflictRequest):


    print("API Python executable:", sys.executable)


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
    if (modelType == 'lightgbm'):
        relative_target_path_dur = '../ConflictCalc/Python/'
        models = load_lightGBM_models(relative_target_path_dur)

        if len(models) == 0:
            raise HTTPException(status_code= 401, detail="LightGBM model was requested, but no LightGBM models have been loaded.")

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
    #end of 