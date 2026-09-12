"""Walk the Minecraft sign-in chain far enough to prove the app exists and is unapproved.

This is the "fail first" step: Microsoft will not review an Azure app for Minecraft API access
until it has seen the app actually try, so the point of this script is to reach
api.minecraftservices.com and collect the 403 that proves the attempt happened.

Prints statuses only - no token is ever written to stdout or to disk.

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


def fail(step, status, body):
    print(f"\nFAILED at {step} - HTTP {status}")
    print(json.dumps(body, indent=2)[:1200] if isinstance(body, dict) else str(body)[:1200])
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
print(json.dumps(mc, indent=2)[:800] if isinstance(mc, dict) else str(mc)[:800])
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
