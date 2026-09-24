@echo off
REM Activate your conda environment
call conda activate base

REM Start the FastAPI server
uvicorn ConflictQuery:app --reload --port 8000

pause
