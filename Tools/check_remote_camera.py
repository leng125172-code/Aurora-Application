import argparse
import os
import posixpath
import shlex
import sys

import paramiko


def run(client: paramiko.SSHClient, command: str) -> tuple[int, str, str]:
    stdin, stdout, stderr = client.exec_command(command, timeout=30)
    return stdout.channel.recv_exit_status(), stdout.read().decode("utf-8", "replace"), stderr.read().decode("utf-8", "replace")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", required=True)
    parser.add_argument("--user", required=True)
    parser.add_argument("--local-dir", required=True)
    args = parser.parse_args()

    password = os.environ.get("AURORA_REMOTE_PASSWORD")
    if not password:
        raise RuntimeError("AURORA_REMOTE_PASSWORD is not set")

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(args.host, username=args.user, password=password, timeout=15)
    try:
        db_password = shlex.quote(password)
        camera_sql = 'SELECT "Id","Name","DeviceIndex","HardwareId","Status" FROM "AbpProCameraDevices" ORDER BY "DeviceIndex";'
        intrinsic_sql = 'SELECT p."BlobKey",p."CapturedAt",c."DeviceIndex",c."Name" FROM "AbpProCalibPhotoRecords" p JOIN "AbpProCameraDevices" c ON c."Id"=p."CameraDeviceId" WHERE p."PhotoType"=0 AND c."Name"=\'相机 #1\' ORDER BY p."CapturedAt" DESC LIMIT 5;'
        commands = {
            "identity": "hostname; uname -a; pwd",
            "processes": "ps -ef | grep -E '[A]uroraStruct3D|[d]otnet|[T]ucam|[c]amera'",
            "camera_devices": "ls -l /dev/video* 2>/dev/null || true",
            "likely_files": "find /home/linaro /opt /tmp -maxdepth 4 -type f \\( -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.bmp' -o -iname '*.png' \\) -printf '%T@ %s %p\\n' 2>/dev/null | sort -nr | head -30",
            "services": "systemctl --no-pager --type=service --state=running 2>/dev/null | grep -Ei 'aurora|camera|tucam' || true",
            "appsettings": "grep -RniE 'ConnectionStrings|Blob|Redis|AuthServer|SelfUrl' /home/linaro/Publish/linux-arm64/AuroraStruct3D.HttpApi.Host/appsettings*.json 2>/dev/null | head -80",
            "recent_calib_images": "find /home/linaro/Publish /var/lib /tmp -type f \\( -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.bmp' -o -iname '*.png' \\) -printf '%T@ %s %p\\n' 2>/dev/null | sort -nr | head -50",
            "camera_api": "curl -sS -o /tmp/camera-list.json -w '%{http_code}' http://127.0.0.1:5000/api/app/camera-device; printf '\\n'; head -c 1000 /tmp/camera-list.json",
            "camera_1_db": f"PGPASSWORD={db_password} psql -h 127.0.0.1 -U postgres -d LionAbpPro10 -AtF '|' -c {shlex.quote(camera_sql)}",
            "latest_intrinsic_db": f"PGPASSWORD={db_password} psql -h 127.0.0.1 -U postgres -d LionAbpPro10 -AtF '|' -c {shlex.quote(intrinsic_sql)}",
            "calib_files": f"printf '%s\\n' {db_password} | sudo -S find / -type f -path '*/calib-photos/*' -printf '%T@ %s %p\\n' 2>/dev/null | sort -nr | head -30",
        }
        results: dict[str, tuple[int, str, str]] = {}
        for name, command in commands.items():
            results[name] = run(client, command)
            code, stdout, stderr = results[name]
            print(f"## {name} (exit={code})")
            print(stdout.rstrip())
            if stderr.strip():
                print(stderr.rstrip(), file=sys.stderr)

        os.makedirs(args.local_dir, exist_ok=True)
        listing = results["calib_files"][1].splitlines()
        candidates = []
        for line in listing:
            parts = line.split(" ", 2)
            if len(parts) == 3 and parts[2].startswith("/"):
                candidates.append(parts[2])

        if candidates:
            remote_path = candidates[0]
            local_path = os.path.join(args.local_dir, posixpath.basename(remote_path))
            with client.open_sftp() as sftp:
                sftp.get(remote_path, local_path)
            print(f"DOWNLOADED={local_path}")
        else:
            print("DOWNLOADED=")
    finally:
        client.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
