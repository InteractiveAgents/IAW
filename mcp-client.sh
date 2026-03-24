#!/bin/bash
# Reusable MCP client for IAW
# Usage:
#   source mcp-client.sh
#   mcp_init                    # get session
#   mcp_call tool_name 'json'   # call a tool (10 min timeout)

MCP_URL="http://localhost:5300"
MCP_SESSION=""

mcp_init() {
  MCP_SESSION=$(curl -s -D - -X POST "$MCP_URL/" \
    -H "Content-Type: application/json" \
    -H "Accept: application/json, text/event-stream" \
    -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"claude","version":"1.0"}}}' 2>&1 | grep "Mcp-Session-Id" | awk '{print $2}' | tr -d '\r\n')

  curl -s -X POST "$MCP_URL/" \
    -H "Content-Type: application/json" \
    -H "Mcp-Session-Id: $MCP_SESSION" \
    -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' > /dev/null 2>&1

  echo "MCP Session: $MCP_SESSION"
}

mcp_call() {
  local tool="$1"
  local args="$2"
  local id="${3:-$(date +%s)}"

  curl -s --max-time 600 -X POST "$MCP_URL/" \
    -H "Content-Type: application/json" \
    -H "Accept: application/json, text/event-stream" \
    -H "Mcp-Session-Id: $MCP_SESSION" \
    -d "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"$tool\",\"arguments\":$args}}" 2>&1 | grep "^data:" | sed 's/^data: //'
}
