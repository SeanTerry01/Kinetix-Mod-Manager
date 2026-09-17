"""Walk the Minecraft sign-in chain far enough to prove the app exists and is unapproved.

This is the "fail first" step: Microsoft will not review an Azure app for Minecraft API access
until it has seen the app actually try, so the point of this script is to reach
api.minecraftservices.com and collect the 403 that proves the attempt happened.

Prints statuses only - no token is ever written to stdout or to disk.

⚠️ That sentence was a LIE until 2026-09-17, and only on the run that mattered. Every poll had come
back 403, whose body carries no credential, so nobody noticed that the success path dumped the whole
response - including a live 24-hour Minecraft access token - straight to stdout, and from there into
whatever file the run was redirected to. The day it finally returned 200, it wrote the token to disk.
Everything credential-shaped now goes through redact(); see SECRET_KEYS.

Chain: Microsoft device code -> Xbox Live -> XSTS -> Minecraft services.
"""

import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

CLIENT_ID = "dfae7b53-ed9a-4573-acb6-02928d7224ac"
TENANT = "consumers"          # personal Microsoft accounts; 'common' does not work here
SCOPE = "XboxLive.signin offline_access"


def post(url, data, headers=None, form=False):
    """POST and return (status, parsed-json-or-text). Never raises on HTTP error status."""
    if form:
        body = urllib.parse.urlencode(data).encode()
        ctype = "application/x-www-form-urlencoded"
    else:
        body = json.dumps(data).encode()
        ctype = "application/json"

    req = urllib.request.Request(url, data=body, method="POST")
    req.add_header("Content-Type", ctype)
    req.add_header("Accept", "application/json")
    for k, v in (headers or {}).items():
        req.add_header(k, v)

    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read().decode("utf-8", "replace")
            status = r.status
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", "replace")
        status = e.code
    except urllib.error.URLError as e:
        return 0, {"network_error": str(e.reason)}

    try:
        return status, json.loads(raw)
    except ValueError:
        return status, raw


# Anything whose value is a credential rather than a status. The Minecraft response carries a LIVE
# access token, good for 24 hours, and the 403 body does not -- so this only ever mattered on success,
# which is exactly the run nobody had done yet when this script was written.
SECRET_KEYS = {"access_token", "refresh_token", "id_token", "Token", "identityToken", "RpsTicket",
               "device_code", "user_code"}


def redact(value):
    """A copy of a response with every credential replaced by its length. Structure stays readable."""
    if isinstance(value, dict):
        return {k: (f"<{len(v)} chars, not shown>" if k in SECRET_KEYS and isinstance(v, str)
                    else redact(v))
                for k, v in value.items()}
    if isinstance(value, list):
        return [redact(v) for v in value]
    return value


def show(body, limit):
    """Print a response body without ever printing a credential."""
    if isinstance(body, dict):
        print(json.dumps(redact(body), indent=2)[:limit])
    else:
        print(str(body)[:limit])


def fail(step, status, body):
    print(f"\nFAILED at {step} - HTTP {status}")
    show(body, 1200)
    sys.exit(1)


# -------------------------------------------------- 1. Microsoft device code

print("Step 1: requesting a device code from Microsoft...")
status, dc = post(
    f"https://login.microsoftonline.com/{TENANT}/oauth2/v2.0/devicecode",
    {"client_id": CLIENT_ID, "scope": SCOPE},
    form=True,
)
if status != 200:
    fail("device code request", status, dc)

print("\n" + "=" * 60)
print("  GO TO:  " + dc["verification_uri"])
print("  CODE :  " + dc["user_code"])
print("=" * 60)
print("\nWaiting for you to sign in (use the MINECRAFT account)...\n")

# -------------------------------------------------- 2. poll for the token

interval = int(dc.get("interval", 5))
deadline = time.time() + int(dc.get("expires_in", 900))
ms_token = None

while time.time() < deadline:
    time.sleep(interval)
    status, tok = post(
        f"https://login.microsoftonline.com/{TENANT}/oauth2/v2.0/token",
        {
            "grant_type": "urn:ietf:params:oauth:grant-type:device_code",
            "client_id": CLIENT_ID,
            "device_code": dc["device_code"],
        },
        form=True,
    )
    if status == 200:
        ms_token = tok["access_token"]
        print("Step 2: Microsoft sign-in OK.")
        break
    err = tok.get("error") if isinstance(tok, dict) else None
    if err == "authorization_pending":
        continue
    if err == "slow_down":
        interval += 5
        continue
    fail("Microsoft token exchange", status, tok)

if not ms_token:
    fail("Microsoft token exchange", 0, {"error": "device code expired"})

# -------------------------------------------------- 3. Xbox Live

print("Step 3: exchanging for an Xbox Live token...")
status, xbl = post(
    "https://user.auth.xboxlive.com/user/authenticate",
    {
        "Properties": {
            "AuthMethod": "RPS",
            "SiteName": "user.auth.xboxlive.com",
            "RpsTicket": "d=" + ms_token,
        },
        "RelyingParty": "http://auth.xboxlive.com",
        "TokenType": "JWT",
    },
)
if status != 200:
    fail("Xbox Live authenticate", status, xbl)
xbl_token = xbl["Token"]
uhs = xbl["DisplayClaims"]["xui"][0]["uhs"]
print("Step 3: Xbox Live OK.")

# -------------------------------------------------- 4. XSTS

print("Step 4: exchanging for an XSTS token for Minecraft...")
status, xsts = post(
    "https://xsts.auth.xboxlive.com/xsts/authorize",
    {
        "Properties": {"SandboxId": "RETAIL", "UserTokens": [xbl_token]},
        "RelyingParty": "rp://api.minecraftservices.com/",
        "TokenType": "JWT",
    },
)
if status != 200:
    fail("XSTS authorize", status, xsts)
xsts_token = xsts["Token"]
print("Step 4: XSTS OK.")

# -------------------------------------------------- 5. Minecraft - the 403 we want

print("Step 5: calling api.minecraftservices.com (this is the one expected to 403)...")
status, mc = post(
    "https://api.minecraftservices.com/authentication/login_with_xbox",
    {"identityToken": f"XBL3.0 x={uhs};{xsts_token}"},
)

print("\n" + "=" * 60)
print(f"  api.minecraftservices.com returned HTTP {status}")
print("=" * 60)
show(mc, 800)
print()

if status == 403:
    print("EXPECTED. The app is registered but not yet approved for the Minecraft API.")
    print("Microsoft has now seen the app attempt a sign-in, which is the prerequisite")
    print("for submitting the approval form at https://aka.ms/mce-reviewappid")
elif status == 200:
    print("UNEXPECTED - and good news. The app already has Minecraft API access;")
    print("no approval form appears to be needed. Online mode can be built right now.")
else:
    print("Neither 200 nor 403 - read the body above before assuming anything.")
