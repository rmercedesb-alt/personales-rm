"""
Servidor de produccion con Waitress.
IIS lo usa como backend via proxy inverso.
"""
import os
from pathlib import Path
from waitress import serve
from dashboard_server import app

HOST   = os.environ.get('DASHBOARD_HOST', '127.0.0.1')
PORT   = int(os.environ.get('DASHBOARD_PORT', '5000'))

if __name__ == '__main__':
    print(f'Dashboard Visitas -> http://{HOST}:{PORT}')
    serve(app, host=HOST, port=PORT, threads=6, channel_timeout=120)
