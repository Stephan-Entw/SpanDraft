"""Read secrets from Bitwarden Secrets Manager on Windows.

The machine account's access token comes from a generic credential in the Windows Credential Manager
and is handed to the `bws` CLI only through that process' environment. Secret values stay in memory.
Error messages never contain the token, secret values or `bws` output.
"""

import ctypes
import json
import os
import shutil
import subprocess
from ctypes import wintypes
from pathlib import Path


CREDENTIAL_TARGET = "BitwardenSecretsManager"
SERVER_URL = "https://vault.bitwarden.eu"
TIMEOUT_SECONDS = 60
_CRED_TYPE_GENERIC = 1
_ERROR_NOT_FOUND = 1168


class SecretsError(RuntimeError):
    pass


class _Credential(ctypes.Structure):
    """CREDENTIALW from wincred.h."""

    _fields_ = [
        ("Flags", wintypes.DWORD), ("Type", wintypes.DWORD), ("TargetName", wintypes.LPWSTR),
        ("Comment", wintypes.LPWSTR), ("LastWritten", wintypes.FILETIME),
        ("CredentialBlobSize", wintypes.DWORD), ("CredentialBlob", ctypes.POINTER(ctypes.c_ubyte)),
        ("Persist", wintypes.DWORD), ("AttributeCount", wintypes.DWORD), ("Attributes", ctypes.c_void_p),
        ("TargetAlias", wintypes.LPWSTR), ("UserName", wintypes.LPWSTR),
    ]


def read_windows_credential(target=CREDENTIAL_TARGET):
    """Return the password of a generic Windows credential."""
    if os.name != "nt":
        raise SecretsError("Reading the Windows Credential Manager requires Windows.")
    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    advapi32.CredReadW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                   ctypes.POINTER(ctypes.POINTER(_Credential))]
    advapi32.CredReadW.restype = wintypes.BOOL
    advapi32.CredFree.argtypes = [ctypes.c_void_p]
    advapi32.CredFree.restype = None
    pointer = ctypes.POINTER(_Credential)()
    if not advapi32.CredReadW(target, _CRED_TYPE_GENERIC, 0, ctypes.byref(pointer)):
        code = ctypes.get_last_error()
        if code == _ERROR_NOT_FOUND:
            raise SecretsError(f"Windows Credential Manager has no generic credential '{target}'. Add it "
                               "under Windows Credentials with the Bitwarden access token as password.")
        raise SecretsError(f"Cannot read the Windows credential '{target}' (Windows error {code}).")
    try:
        blob = ctypes.string_at(pointer.contents.CredentialBlob, pointer.contents.CredentialBlobSize)
    finally:
        advapi32.CredFree(pointer)
    try:
        # Credential Manager and cmdkey store passwords as UTF-16.
        password = blob.decode("utf-16-le").strip("\0").strip()
    except UnicodeDecodeError:
        password = ""
    if not password:
        raise SecretsError(f"The Windows credential '{target}' has no readable password.")
    return password


def find_bws(environ=os.environ, which=shutil.which):
    found = which("bws")
    if found:
        return found
    local = environ.get("LOCALAPPDATA")
    if local:
        fallback = Path(local) / "Programs" / "Bitwarden" / "bws.exe"
        if fallback.is_file():
            return str(fallback)
    raise SecretsError("The Bitwarden Secrets Manager CLI (bws.exe) was not found on PATH or in "
                       r"%LOCALAPPDATA%\Programs\Bitwarden.")


def _bws_json(executable, token, arguments, run):
    command = [executable, *arguments, "--output", "json", "--color", "no", "--server-url", SERVER_URL]
    try:
        result = run(command, env={**os.environ, "BWS_ACCESS_TOKEN": token}, stdin=subprocess.DEVNULL,
                     capture_output=True, encoding="utf-8", errors="replace", timeout=TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        raise SecretsError(f"bws did not answer within {TIMEOUT_SECONDS} seconds.") from None
    except OSError as error:
        raise SecretsError(f"bws could not be started: {error.strerror}.") from None
    name = " ".join(arguments[:2])
    if result.returncode != 0:
        raise SecretsError(f"'bws {name}' failed with exit code {result.returncode}. Check the network, the "
                           f"access token (expired or revoked?), its project access and the server {SERVER_URL}.")
    try:
        data = json.loads(result.stdout)
    except ValueError:
        data = None
    if not isinstance(data, list) or not all(isinstance(item, dict) for item in data):
        raise SecretsError(f"'bws {name}' returned unexpected output.")
    return data


def read_secrets(project, keys, credential=read_windows_credential, find=find_bws, run=subprocess.run):
    """Return {key: value} for the requested secrets of one project.

    bws can only look secrets up by ID or list them, so the list is limited to this project and
    everything except the requested keys is discarded.
    """
    token = credential()
    executable = find()
    projects = [p for p in _bws_json(executable, token, ["project", "list"], run) if p.get("name") == project]
    if len(projects) != 1 or not projects[0].get("id"):
        raise SecretsError(f"The access token must see exactly one Bitwarden project named '{project}', "
                           f"found {len(projects)}. Check the machine account's project access.")
    values = {}
    for item in _bws_json(executable, token, ["secret", "list", str(projects[0]["id"])], run):
        key = item.get("key")
        if key not in keys:
            continue
        if key in values:
            raise SecretsError(f"Bitwarden project '{project}' contains the secret {key} more than once.")
        values[key] = str(item.get("value") or "").strip()
    missing = [key for key in keys if not values.get(key)]
    if missing:
        raise SecretsError(f"Bitwarden project '{project}' lacks a value for: {', '.join(missing)}.")
    return values
