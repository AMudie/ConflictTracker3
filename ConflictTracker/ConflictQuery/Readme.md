# ConflictQuery

ConflictQuery is the Python based API for the ConflictTracker project. It implements 2x http endpoints:
* functionality-check is a heartbeat, allowing the running status of the server to be confirmed. It is always expected to return status 200 ("ok")
* conflictquery is the main endpoint used for accessing prediction functionality. It requires a country and place string parameter, and a periodStart date parameter that will be used as the first period (i.e, the period containing this date and subsequent 11) to be predicted for and modelType string parameter: Only "lightgbm" and "chronos2" models are supported.
* * Note that conflictquery will include as part of the returned object a dictionary in the format (key, value), where the key is LOCAL_X or REGIONAL_x (depending on the scope of the prediction) and x is the index of the period (so the first is usually 1, the last 12). Up to 24 values will be included for chronos 2 models, as these predict multiple horizons at once. For LightGBM, there will be 8x values: local and regional predictions at horizons 1, 3, 6 and 12 periods.
* * One period is normally 4 weeks (28 days), but early work was done on 12 week periods. It should not be assumed to be functional though.
ConflictQuery has also got MCP decoration around the endpoints to enable LLM access, but this remains untested. 

## Registering as an MCP server in VS Code
### Adding the server
* ctrl + shift + P 
* Add MCP Server
* Stdio
* Entet path to application python executable (there is a python instance for this app), path to ConflictQuery.py and --mcp arguments
C:/Users/andre/source/repos/ConflictTracker3/ConflictTracker/ConflictQuery/.venv/Scripts/python.exe C:/Users/andre/source/repos/ConflictTracker3/ConflictTracker/ConflictQuery/ConflictQuery.py --mcp

### Registering the server
* Ctrl + shift + P
* List Servers
* Find the server added (likely a guid name)
* Start the server
* Check the output: should be 1x tool registered. 

Registered MCP servers can be seen in C:\Users\andre\AppData\Roaming\Code\User\mcp.json. 
