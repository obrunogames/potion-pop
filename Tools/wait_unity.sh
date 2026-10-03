#!/usr/bin/env bash
# Blocks until the Unity editor's MCP stdio bridge is up and not reloading (after compiles / entering play mode).
sleep "${1:-2}"
until python3 -c "
import socket,json,glob
f=glob.glob('$HOME/.unity-mcp/unity-mcp-status-*.json')[0]; d=json.load(open(f))
assert not d.get('reloading', False)
s=socket.create_connection(('127.0.0.1', d.get('unity_port', 6400)), timeout=1); s.close()
" 2>/dev/null; do sleep 1; done
echo ready
