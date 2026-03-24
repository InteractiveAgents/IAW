#!/bin/bash
# Usage: ./mcp-call.sh <session-id> <tool-name> <json-args> [request-id]
SESSION_ID="$1"
TOOL_NAME="$2"
ARGS="$3"
REQ_ID="${4:-1}"

curl -s -X POST http://localhost:5300/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: $SESSION_ID" \
  -d "{\"jsonrpc\":\"2.0\",\"id\":$REQ_ID,\"method\":\"tools/call\",\"params\":{\"name\":\"$TOOL_NAME\",\"arguments\":$ARGS}}" 2>&1 | grep "^data:" | sed 's/^data: //'
