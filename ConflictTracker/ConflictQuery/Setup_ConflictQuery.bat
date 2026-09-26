@echo off
echo Setting up environment...

REM Create a virtual environment inside the project
python -m venv .venv

REM Activate it
call .venv\Scripts\activate

REM Install required packages
pip install fastapi
pip install "uvicorn[standard]"
pip install pydantic
pip install joblib
pip install lightgbm
pip install pandas
pip install numpy
pip install scikit-learn
pip install fastparquet


echo Setup complete.
pause
