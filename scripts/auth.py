"""Dataverse authentication for repository provisioning scripts.

This workspace uses the signed-in Azure CLI identity because the tenant blocks
device-code authentication. No access tokens or credentials are persisted by
this module.
"""

import os
import re
from pathlib import Path


_ALLOWED_SKILLS = frozenset(
    {
        "dv-overview",
        "dv-connect",
        "dv-data",
        "dv-query",
        "dv-metadata",
        "dv-solution",
        "dv-admin",
        "dv-security",
        "unknown",
    }
)
_ALLOWED_AGENTS = frozenset(
    {"claude-code", "copilot", "cursor", "codex", "unknown"}
)
_CONTEXT_RE = re.compile(
    r"^[a-zA-Z0-9_-]+=[a-zA-Z0-9_./-]+"
    r"(;[a-zA-Z0-9_-]+=[a-zA-Z0-9_./-]+)*$"
)


def load_env():
    script_dir = Path(__file__).resolve().parent
    candidates = [script_dir.parent / ".env", Path(".env")]
    env_path = next((path for path in candidates if path.exists()), None)
    if env_path is None:
        raise RuntimeError("Missing .env at the repository root.")

    for line in env_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            key, _, value = line.partition("=")
            os.environ.setdefault(key.strip(), value.strip())


def _operation_context(skill):
    load_env()
    if skill not in _ALLOWED_SKILLS:
        raise ValueError(f"Unsupported Dataverse skill context: {skill}")

    agent = os.environ.get("DATAVERSE_PLUGIN_AGENT", "unknown")
    if agent not in _ALLOWED_AGENTS:
        raise ValueError(f"Unsupported agent context: {agent}")

    version = os.environ.get("DATAVERSE_PLUGIN_VERSION", "unknown")
    value = f"app=dataverse-skills/{version};skill={skill};agent={agent}"
    if not _CONTEXT_RE.fullmatch(value):
        raise ValueError("Invalid Dataverse operation context.")
    return value


def _credential():
    load_env()
    tenant_id = os.environ["TENANT_ID"]
    client_id = os.environ.get("CLIENT_ID", "").strip()
    client_secret = os.environ.get("CLIENT_SECRET", "").strip()

    if client_id and client_secret:
        from azure.identity import ClientSecretCredential

        return ClientSecretCredential(
            tenant_id=tenant_id,
            client_id=client_id,
            client_secret=client_secret,
        )

    from azure.identity import AzureCliCredential

    return AzureCliCredential(tenant_id=tenant_id)


def get_client(skill, **kwargs):
    load_env()
    from PowerPlatform.Dataverse.client import DataverseClient
    from PowerPlatform.Dataverse.core.config import OperationContext

    return DataverseClient(
        base_url=os.environ["DATAVERSE_URL"],
        credential=_credential(),
        context=OperationContext(user_agent_context=_operation_context(skill)),
        **kwargs,
    )


def get_token(scope=None):
    load_env()
    dataverse_url = os.environ["DATAVERSE_URL"].rstrip("/")
    requested_scope = scope or f"{dataverse_url}/.default"
    return _credential().get_token(requested_scope).token


def get_plugin_headers(skill, token=None):
    context = _operation_context(skill)
    headers = {"User-Agent": f"Python-urllib ({context})"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers


if __name__ == "__main__":
    load_env()
    credential = _credential()
    scope = f"{os.environ['DATAVERSE_URL'].rstrip('/')}/.default"
    access_token = credential.get_token(scope)
    print(
        "Connected: "
        f"{os.environ['DATAVERSE_URL']} "
        f"tenant={os.environ['TENANT_ID']} "
        f"expires_on={access_token.expires_on}"
    )
