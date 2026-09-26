from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from datetime import datetime
import joblib 
import sklearn
import os
import pandas as pd

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



#conflictquery enddpoing designed for c# consumption:
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

#This method needs to:
#1. Load the features from the presaved dataset (parquet) into a pandas dataframe of 1x item
#2. Drop extra features (e.g. targets for the other models)
#3. Use the supplied models to make a prediction
#4. Add the prediction to predictions variable
def lightgbm_predictions(lightgbm_models: dict, country: str, place: str, datetime: datetime):
    predictions = {}

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
        predictions[target_name] = pred

        print(pred)
    #return predictions   

    return predictions
    #End of lightgbm_predictions

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

    #test only: not for production.
    #for col in df.columns:
    #    print(f"{col}: {df[col].dtype}")

    #test only: not for production.
    #for col in df.columns:
        #print(f"{col}: {df[col].dtype}")


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
        else:
            predictions = lightgbm_predictions(models, req.country, req.place, req.periodStart)
            #Example: South Sudan/Aduel/South Sudan/26/09/2024


    if (req.modelType == 'chronos2'):
        raise HTTPException( status_code = 501, detail = f'{req.model} is not yet implemented.')

    return ConflictResponse(
        place=req.place,
        country=req.country,
        periodStart=req.periodStart.isoformat(),
        predictions = predictions
    )
