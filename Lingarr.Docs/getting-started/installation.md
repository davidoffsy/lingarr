# Installation

This page documents the [davidoffsy/lingarr-support-mkv](https://github.com/davidoffsy/lingarr-support-mkv) fork of Lingarr, which adds MKV embedded text-subtitle extraction.

Lingarr runs as a Docker container. This page covers the available images and how to set up Lingarr with Docker Compose or the Docker CLI.

## Docker images

Lingarr provides multi-architecture Docker images that automatically select the correct platform:

| Tag | Description | Architectures |
|-----|-------------|---------------|
| `latest` | Latest stable release | `amd64` `arm64` |
| `1.2.3` | Specific version | `amd64` `arm64` |
| `main` | ⚠️ Development build from main branch | `amd64` `arm64` |

Note: As of 1.0.3 all images support both AMD64 (Intel/AMD) and ARM64 (Raspberry Pi, Apple Silicon) architectures. Docker will automatically pull the correct architecture for your system.

This fork publishes multi-arch images to GitHub Container Registry only:

| Registry | Image |
|----------|-------|
| GitHub Container Registry | `ghcr.io/davidoffsy/lingarr-support-mkv:latest` |

Until a GitHub Release is published, use the `main` tag (`ghcr.io/davidoffsy/lingarr-support-mkv:main`).

## Docker Compose

By default, Lingarr uses `MySQL`, however, `PostgreSQL` and `SQLite` are also supported. Note that Lingarr can quickly, if migrations fail, using a `healthcheck` combined with `depends_on` is recommended.

```yaml
services:
  lingarr:
    image: ghcr.io/davidoffsy/lingarr-support-mkv:main
    container_name: lingarr
    restart: unless-stopped
    environment:
      - ASPNETCORE_URLS=http://+:9876
      - DB_CONNECTION=mysql
      - DB_HOST=lingarr-db
      - DB_PORT=3306
      - DB_DATABASE=lingarr
      - DB_USERNAME=lingarr
      - DB_PASSWORD=your-password # provide a password
    ports:
      - "9876:9876"
    volumes:
      - /path/to/media/movies:/movies
      - /path/to/media/tv:/tv
      - /path/to/config:/app/config
    networks:
      - lingarr
    depends_on:
      lingarr-db:
        condition: service_healthy

  lingarr-db:
    image: mariadb:latest
    container_name: lingarr-db
    restart: unless-stopped
    environment:
      - MYSQL_DATABASE=lingarr
      - MYSQL_USER=lingarr
      - MYSQL_PASSWORD=your-password # provide a password
      - MYSQL_ROOT_PASSWORD=your-password # provide a root password
    volumes:
      - /path/to/db:/var/lib/mysql # define a volume to persist the database
    networks:
      - lingarr
    healthcheck:
      test: "mariadb $$MYSQL_DATABASE -u$$MYSQL_USER -p$$MYSQL_PASSWORD -e 'SELECT 1;'"
      interval: 10s
      timeout: 5s
      retries: 5

networks:
  lingarr:
    external: true
```

## Docker CLI

Follow these steps to set up Lingarr via the Docker CLI:

```bash
# Create the network
docker network create lingarr

# Start MariaDB
docker run -d \
  --name lingarr-db \
  --restart unless-stopped \
  -e MYSQL_DATABASE=lingarr \
  -e MYSQL_USER=lingarr \
  -e MYSQL_PASSWORD=your-password \
  -e MYSQL_ROOT_PASSWORD=your-root-password \
  -v /path/to/db:/var/lib/mysql \
  --network lingarr \
  mariadb:latest

# Start Lingarr
docker run -d \
  --name lingarr \
  --restart unless-stopped \
  -p 9876:9876 \
  -e ASPNETCORE_URLS=http://+:9876 \
  -e DB_CONNECTION=mysql \
  -e DB_HOST=lingarr-db \
  -e DB_PORT=3306 \
  -e DB_DATABASE=lingarr \
  -e DB_USERNAME=lingarr \
  -e DB_PASSWORD=your-password \
  -v /path/to/media/movies:/movies \
  -v /path/to/media/tv:/tv \
  -v /path/to/config:/app/config \
  --network lingarr \
  ghcr.io/davidoffsy/lingarr-support-mkv:main
```

## FFmpeg

The Lingarr image installs FFmpeg (including `ffprobe`) so it can inspect and extract text subtitle tracks from MKV files. This increases the image size compared to a plain ASP.NET runtime.

When you enable **Scan embedded MKV subtitles** in Settings → Subtitle, or use the **MKV** button on a movie / episode to extract a track on demand, mount the folders that contain the **video files** themselves (the same `/movies` and `/tv` volumes as Radarr/Sonarr), and ensure the container user can read those files. Sidecar translation does not require FFmpeg; if the binaries are missing, Lingarr logs a warning and skips embedded tracks.

Extracting a text track copies subtitle packets from the video. On a cloud / rclone / CloudDrive mount that usually means reading the **entire** file, which can take a long time. The Translations page shows extract progress (0–20%) then translation (20–100%). Set **Embedded extract timeout** (default 60 minutes) in Settings → Subtitle for on-demand extracts. Automatic / bulk extract stays at 2 minutes. SQLite Hangfire uses `max(JOB_TIMEOUT_MINUTES, EXTRACT_TIMEOUT_MINUTES)` so the worker is not treated as dead mid-extract. Cancel the request if a remux is still too slow.

Local development outside Docker can install FFmpeg on the host, or point `FFMPEG_PATH` / `FFPROBE_PATH` at custom binaries.

## Running as non-root

When running Docker containers, you can optionally specify which user should run the container process using the `user` flag. This is useful for:

- **Security**: Running containers as non-root users
- **Permissions**: Ensuring files created by the container have the correct ownership on your host system

**Docker Compose syntax:**
```yaml
user: "1000:1000"  # UID:GID
```

**Docker CLI syntax:**
```bash
--user 1000:1000  # UID:GID
```
