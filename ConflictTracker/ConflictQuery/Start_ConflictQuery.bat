::VERSION 1.0.0: Anaconda Environment
::@echo off
::REM Activate your conda environment
::call conda activate base
 
::REM Start the FastAPI server
::uvicorn ConflictQuery:app --reload --port 8000

::pause

::VERSION 2.0.0: Standalone Environment
::@echo off
::call .venv\Scripts\activate
::uvicorn ConflictQuery:app --reload --port 8000
::pause

::Version 3.0.0: Standalone Env with automated dependency install:

@echo off
echo Starting ConflictQuery Server

REM Check if the virtual environment exists
IF NOT EXIST ".venv\Scripts\activate" (
    echo No environment found. Running setup...
    call Setup_ConflictQuery.bat

    REM Check if setup succeeded
    IF %ERRORLEVEL% NEQ 0 (
        echo ERROR: Setup failed. Cannot start server.
        pause
        exit /b 1
    )
) ELSE (
    echo All dependencies already installed.
)

echo Activating environment...
call .venv\Scripts\activate

IF %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to activate environment.
    pause
    exit /b 1
)

::open a web page at the /docs page, this is useful for testing. 
start http://localhost:8000/docs



echo Starting FastAPI server...
uvicorn ConflictQuery:app --reload --port 8000

IF %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to start server.
    pause
    exit /b 1
)

echo Server stopped.
pause
