from __future__ import annotations

import json
import queue
import socket
import threading
from dataclasses import dataclass

from ..shared.protocol import encode_message, make_response


@dataclass
class PendingRequest:
    request: dict
    event: threading.Event
    response: dict | None = None


class WorkerServer:
    def __init__(self, host: str, port: int, token: str, runtime):
        self._host = host
        self._port = port
        self._token = token
        self._runtime = runtime
        self._pending_requests: queue.Queue[PendingRequest] = queue.Queue()
        self._stop_event = threading.Event()
        self._server_socket = None
        self._thread = None

    @property
    def pending_requests(self):
        return self._pending_requests

    def start(self):
        self._server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._server_socket.bind((self._host, self._port))
        self._server_socket.listen(5)
        self._server_socket.settimeout(0.5)

        self._thread = threading.Thread(target=self._accept_loop, name="BRQWorkerServer", daemon=True)
        self._thread.start()

    def stop(self):
        self._stop_event.set()
        if self._server_socket is not None:
            try:
                self._server_socket.close()
            except Exception:
                pass
        if self._thread is not None:
            self._thread.join(timeout=1.0)

    def _accept_loop(self):
        while not self._stop_event.is_set():
            try:
                client, _ = self._server_socket.accept()
            except socket.timeout:
                continue
            except OSError:
                break

            thread = threading.Thread(target=self._handle_client, args=(client,), daemon=True)
            thread.start()

    def _handle_client(self, client_socket: socket.socket):
        with client_socket:
            try:
                raw_request = self._recv_line(client_socket)
                request = json.loads(raw_request)

                if request.get("token") != self._token:
                    response = make_response(
                        request.get("request_id"),
                        False,
                        self._runtime.state.status,
                        error="Invalid worker token",
                    )
                    client_socket.sendall(encode_message(response))
                    return

                pending = PendingRequest(request=request, event=threading.Event())
                self._pending_requests.put(pending)
                pending.event.wait(timeout=60.0)

                response = pending.response or make_response(
                    request.get("request_id"),
                    False,
                    self._runtime.state.status,
                    error="Timed out waiting for Blender main thread",
                )
                client_socket.sendall(encode_message(response))
            except Exception as exc:
                response = make_response(None, False, self._runtime.state.status, error=str(exc))
                client_socket.sendall(encode_message(response))

    @staticmethod
    def _recv_line(client_socket: socket.socket) -> str:
        buffer = bytearray()
        while True:
            chunk = client_socket.recv(4096)
            if not chunk:
                break
            buffer.extend(chunk)
            if b"\n" in chunk:
                break
        return buffer.decode("utf-8").strip()
