"""Build the Rust client (client-rs/) for the desktop or the browser.

Usage:
    python tools/build_client_rs.py
        build/client-rs/native: betterskillys(.exe) with its assets/ folder beside it.

    python tools/build_client_rs.py --web
        build/client-rs/web: index.html, the wasm module and assets/, ready for any static web
        server (python -m http.server, nginx...). Needs the wasm32-unknown-unknown target and
        wasm-bindgen-cli at the version in client-rs/Cargo.lock.

    --debug builds without optimisation (much slower to run).

    python tools/build_client_rs.py --account http://188.141.12.3:8080 --package
        As above with that account server built in, then dist/Do-Damage-<os>.zip (the
        game and a short readme) to hand to other players, as build_client.py --package does
        for the AIR client. With --web, the page's files go in the zip instead.

    python tools/build_client_rs.py --account http://188.141.12.3:8080 --publish
        As above, then publish it to dist/client, which the account server serves at /client/:
        players' clients (packaged or published builds) download it in the background and
        switch to it at their next portal. Needs a section for the version (client-rs/Cargo.toml)
        in client-rs/CHANGELOG.md. The release is signed with build/client-update.key (made on
        the first publish; keep it: clients only take releases signed with it). --publish-to DIR
        publishes somewhere else (an isolated test stack's build/iso<N>/client).
        Roll back with: cargo run -p bsk-publish -- rollback --to dist/client --version <x.y.z>

Run tools/gen_rs_manifest.py first if the AIR client's asset lists changed.
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CRATE = os.path.join(ROOT, "client-rs")
SRC = os.path.join(ROOT, "client", "src")
MANIFEST = os.path.join(CRATE, "assets", "manifest.json")
BIN = "betterskillys"
UPDATE_KEY = os.path.join(ROOT, "build", "client-update.key")


def run(cmd, **kw):
    print(">", " ".join(cmd), flush=True)
    subprocess.run(cmd, check=True, **kw)


def manifest_files():
    with open(MANIFEST, encoding="utf-8") as f:
        m = json.load(f)
    files = set()
    for entry in m["image_sets"] + m["animated_chars"] + m["models"]:
        files.update(p for p in (entry.get("file"), entry.get("mask")) if p)
    files.update(m["ground_xml"] + m["object_xml"] + m["region_xml"])
    files.update([m["particles"], m["fonts"]["regular"], m["fonts"]["bold"]])
    files.update(m.get("screens", {}).values())
    return sorted(files)


def copy_assets(out):
    """assets/manifest.json plus every file it names, at the same relative paths."""
    assets = os.path.join(out, "assets")
    if os.path.isdir(assets):
        shutil.rmtree(assets)
    os.makedirs(assets)
    shutil.copy2(MANIFEST, os.path.join(assets, "manifest.json"))
    files = manifest_files()
    for rel in files:
        dst = os.path.join(assets, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(os.path.join(SRC, rel), dst)
    print(f"{len(files)} asset files copied")


def bindgen_version():
    with open(os.path.join(CRATE, "Cargo.lock"), encoding="utf-8") as f:
        m = re.search(r'name = "wasm-bindgen"\nversion = "([^"]+)"', f.read())
    return m.group(1)


def version():
    """The client's version, from client-rs/Cargo.toml: [workspace.metadata] release when there is
    one (a four-part version such as 1.1.3.1, which Cargo can't take), else the package version."""
    with open(os.path.join(CRATE, "Cargo.toml"), encoding="utf-8") as f:
        text = f.read()
    package = re.search(r'\[workspace\.package\][^\[]*?version = "([^"]+)"', text).group(1)
    m = re.search(r'\[workspace\.metadata\][^\[]*?release = "([^"]+)"', text)
    if not m:
        return package
    if not m.group(1).startswith(package + "."):
        sys.exit(f"client-rs/Cargo.toml: release {m.group(1)} doesn't extend version {package}; "
                 "take it out (or fix it) when the version is raised")
    return m.group(1)


def commit():
    """The commit being built (short hash), or "" outside git."""
    r = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=ROOT, capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else ""


def publish_tool(*args):
    """Run bsk-publish; returns what it printed. Cargo's progress (building it the first time)
    shows as it goes."""
    cmd = ["cargo", "run", "--release", "-p", "bsk-publish", "--"] + list(args)
    print(">", " ".join(cmd), flush=True)
    return subprocess.run(cmd, cwd=CRATE, check=True, stdout=subprocess.PIPE, text=True).stdout.strip()


def update_key():
    """The public key releases are signed with (made with its private key on first use)."""
    return publish_tool("key", "--key", UPDATE_KEY)


def cargo_env(account, key=None):
    """The environment for cargo: BSK_ACCOUNT builds that account server in, BSK_UPDATE_KEY the key
    updates must be signed with (builds for players only), BSK_COMMIT the commit, BSK_VERSION the
    version the game calls itself."""
    env = dict(os.environ)
    if account:
        env["BSK_ACCOUNT"] = account
    env["BSK_UPDATE_KEY"] = key or ""
    env["BSK_COMMIT"] = commit()
    env["BSK_VERSION"] = version()
    return env


README = """Do Damage
=========

Run {exe} to play. Keep the assets folder beside it.

The game connects to {account}.
To play on another server, start it with: {exe} --account http://<address>:8080

Settings and a remembered login are kept in {store}.
"""


def package(out, web, account):
    """dist/Do-Damage-<os>.zip: the built files and a readme, in a "Do Damage" folder."""
    import platform
    import zipfile
    name = "web" if web else {"Windows": "windows", "Darwin": "macos"}.get(platform.system(), "linux")
    dist = os.path.join(ROOT, "dist")
    os.makedirs(dist, exist_ok=True)
    path = os.path.join(dist, f"Do-Damage-{name}.zip")
    exe = BIN + (".exe" if os.name == "nt" and not web else "")
    store = {"windows": "%APPDATA%\\betterSkillys-rs", "macos": "~/Library/Application Support/betterSkillys-rs",
             "linux": "~/.config/betterSkillys-rs", "web": "the browser's storage"}[name]
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for dirpath, _, files in os.walk(out):
            for f in files:
                full = os.path.join(dirpath, f)
                z.write(full, os.path.join("Do Damage", os.path.relpath(full, out)))
        if not web:
            z.writestr("Do Damage/README.txt", README.format(exe=exe, account=account or "this PC (127.0.0.1:8080)", store=store))
    print("packaged", os.path.relpath(path, ROOT), f"({os.path.getsize(path) // 1024} KB)")


def build_native(release, out, account=None, key=None):
    profile = "release" if release else "debug"
    run(["cargo", "build", "--bin", BIN] + (["--release"] if release else []), cwd=CRATE, env=cargo_env(account, key))
    exe = BIN + (".exe" if os.name == "nt" else "")
    os.makedirs(out, exist_ok=True)
    shutil.copy2(os.path.join(CRATE, "target", profile, exe), os.path.join(out, exe))
    copy_assets(out)


def build_web(release, out, account=None, key=None):
    want = bindgen_version()
    try:
        have = subprocess.run(["wasm-bindgen", "--version"], capture_output=True, text=True).stdout.split()[-1]
    except (FileNotFoundError, IndexError):
        have = None
    if have != want:
        sys.exit(f"wasm-bindgen-cli {want} is needed (found {have}): "
                 f"cargo install wasm-bindgen-cli --version {want} --locked")
    profile = "release" if release else "debug"
    run(["cargo", "build", "--bin", BIN, "--target", "wasm32-unknown-unknown"]
        + (["--release"] if release else []), cwd=CRATE, env=cargo_env(account))
    os.makedirs(out, exist_ok=True)
    wasm = os.path.join(CRATE, "target", "wasm32-unknown-unknown", profile, BIN + ".wasm")
    run(["wasm-bindgen", "--target", "web", "--no-typescript", "--out-dir", out, wasm])
    copy_assets(out)
    # The page's loading bar: what a first visit downloads (the files, before compression).
    wasm_bytes = os.path.getsize(os.path.join(out, BIN + "_bg.wasm"))
    with open(MANIFEST, encoding="utf-8") as f:
        unused = set(json.load(f).get("screens", {}).values())  # the old title screen's art isn't loaded
    asset_bytes = os.path.getsize(MANIFEST) + sum(os.path.getsize(os.path.join(SRC, rel)) for rel in manifest_files() if rel not in unused)
    with open(os.path.join(CRATE, "web", "index.html"), encoding="utf-8") as f:
        page = (f.read().replace("BSK_BUILD", f"{version()}-{commit()}")
                .replace("BSK_WASM_BYTES", str(wasm_bytes)).replace("BSK_ASSET_BYTES", str(asset_bytes)))
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8") as f:
        f.write(page)
    n = gzip_files(out)
    print(f"{n} files gzipped beside themselves, for the gateway to send compressed")


# Files worth compressing; images are compressed already.
GZIP_TYPES = (".wasm", ".js", ".html", ".xml", ".json", ".dat", ".otf", ".ttf", ".txt")


def gzip_files(out):
    """A .gz copy of each compressible file (at the strongest level, once, at build time), which
    the gateway sends to browsers that take gzip (ServeDir::precompressed_gzip)."""
    import gzip
    n = 0
    for d, _, files in os.walk(out):
        for f in files:
            if f.endswith(GZIP_TYPES):
                path = os.path.join(d, f)
                with open(path, "rb") as src, gzip.open(path + ".gz", "wb", compresslevel=9) as dst:
                    shutil.copyfileobj(src, dst)
                n += 1
    return n


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--web", action="store_true", help="build for the browser")
    parser.add_argument("--debug", action="store_true", help="unoptimised build")
    parser.add_argument("--account", help="the account server to build in, e.g. http://188.141.12.3:8080")
    parser.add_argument("--package", action="store_true", help="also zip it into dist/ for other players")
    parser.add_argument("--publish", action="store_true", help="also publish it for players' clients to update to")
    parser.add_argument("--publish-to", help="where to publish (default dist/client)")
    args = parser.parse_args()
    if args.publish and args.web:
        sys.exit("--publish is for the desktop build (browsers load the newest files when the page loads)")
    if args.publish and not args.account:
        # without it the release has http://127.0.0.1:8080 built in: players who restart the game
        # after updating can't reach the servers, or the next update (2.1.1 and 2.2.0 did that)
        sys.exit("--publish needs --account: the account server players' clients use, such as "
                 "https://dodmg.com/api")
    out = os.path.join(ROOT, "build", "client-rs", "web" if args.web else "native")
    # Builds for players check for updates, signed with this key.
    key = update_key() if (args.package or args.publish) and not args.web else None
    (build_web if args.web else build_native)(not args.debug, out, args.account, key)
    print("built", os.path.relpath(out, ROOT), f"(version {version()})")
    if args.package:
        package(out, args.web, args.account)
    if args.publish:
        to = os.path.abspath(args.publish_to or os.path.join(ROOT, "dist", "client"))
        print(publish_tool("release", "--key", UPDATE_KEY, "--from", out, "--to", to,
                           "--changelog", os.path.join(CRATE, "CHANGELOG.md"), "--version", version(), "--commit", commit()))


if __name__ == "__main__":
    main()
