namespace SCOverlay.BrowserSource;

internal static class BrowserSourcePage
{
    public const string AssetManifestJson = """
        {
          "assets": [
            {
              "id": "roll-indicator-default",
              "path": "/assets/roll-indicator-default.svg",
              "type": "image/svg+xml"
            },
            {
              "id": "roll-indicator-gladius",
              "path": "/assets/roll-indicator-gladius.png",
              "type": "image/png"
            },
            {
              "id": "roll-indicator-arrow",
              "path": "/assets/roll-indicator-arrow.png",
              "type": "image/png"
            }
          ]
        }
        """;

    public const string RollIndicatorSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="-80 -80 160 160">
          <path d="M0,-62 L19,-12 L56,13 L18,19 L0,62 L-18,19 L-56,13 L-19,-12 Z" fill="white" opacity="0.96"/>
          <path d="M0,-42 L11,-9 L31,5 L9,7 L0,32 L-9,7 L-31,5 L-11,-9 Z" fill="none" stroke="black" stroke-width="4" opacity="0.25"/>
        </svg>
        """;

    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>SC Overlay OBS</title>
          <style>
            html, body {
              margin: 0;
              width: 100%;
              height: 100%;
              overflow: hidden;
              background: transparent;
              font-family: "Segoe UI", Arial, Helvetica, sans-serif;
            }

            canvas {
              display: block;
              width: 100vw;
              height: 100vh;
            }
          </style>
        </head>
        <body>
          <canvas id="overlay"></canvas>
          <script>
            const canvas = document.getElementById('overlay');
            const ctx = canvas.getContext('2d');
            const designWidth = 900;
            const designHeight = 520;
            let latestState = null;
            let lastFetchAt = 0;
            const rollImages = new Map();

            function resize() {
              const dpr = Math.max(window.devicePixelRatio || 1, 1);
              const width = Math.max(window.innerWidth, 1);
              const height = Math.max(window.innerHeight, 1);
              canvas.width = Math.floor(width * dpr);
              canvas.height = Math.floor(height * dpr);
              ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            }

            window.addEventListener('resize', resize);
            resize();

            async function fetchState() {
              try {
                const response = await fetch('/state', { cache: 'no-store' });
                if (response.ok) {
                  latestState = await response.json();
                }
              } catch {
              }
            }

            function color(c, alpha = 1) {
              if (!c) return `rgba(255,255,255,${alpha})`;
              const a = Math.max(0, Math.min(1, (c.a / 255) * alpha));
              return `rgba(${c.r},${c.g},${c.b},${a})`;
            }

            function alpha(c) {
              return c ? Math.max(0, Math.min(1, c.a / 255)) : 1;
            }

            function applyShadow(effects) {
              ctx.shadowColor = 'transparent';
              ctx.shadowBlur = 0;
              ctx.shadowOffsetX = 0;
              ctx.shadowOffsetY = 0;
              if (!effects?.shadowEnabled) return;
              ctx.shadowColor = color(effects.shadowColor, 1);
              ctx.shadowBlur = Math.max(0, effects.shadowWidth || 0);
              ctx.shadowOffsetX = effects.shadowOffsetX || 0;
              ctx.shadowOffsetY = effects.shadowOffsetY || 0;
            }

            function roundedRect(x, y, width, height, radius) {
              const r = Math.max(0, Math.min(radius || 0, width / 2, height / 2));
              ctx.beginPath();
              ctx.moveTo(x + r, y);
              ctx.lineTo(x + width - r, y);
              ctx.quadraticCurveTo(x + width, y, x + width, y + r);
              ctx.lineTo(x + width, y + height - r);
              ctx.quadraticCurveTo(x + width, y + height, x + width - r, y + height);
              ctx.lineTo(x + r, y + height);
              ctx.quadraticCurveTo(x, y + height, x, y + height - r);
              ctx.lineTo(x, y + r);
              ctx.quadraticCurveTo(x, y, x + r, y);
              ctx.closePath();
            }

            function draw() {
              const now = performance.now();
              if (now - lastFetchAt > 33) {
                lastFetchAt = now;
                fetchState();
              }

              const width = window.innerWidth;
              const height = window.innerHeight;
              ctx.clearRect(0, 0, width, height);
              ctx.save();
              const scale = Math.max(Math.min(width / designWidth, height / designHeight), 0.01);
              ctx.translate(width / 2, height / 2);
              ctx.scale(scale, scale);

              if (latestState?.widgets) {
                for (const widget of latestState.widgets) {
                  drawWidget(widget);
                }
              }

              ctx.restore();
              requestAnimationFrame(draw);
            }

            function drawWidget(widget) {
              ctx.save();
              ctx.translate(widget.x || 0, widget.y || 0);
              ctx.globalAlpha = widget.connected ? 1 : 0.32;
              applyShadow(widget.visualEffects);

              switch (widget.type) {
                case 'stick':
                  drawStick(widget);
                  break;
                case 'throttle':
                  drawThrottle(widget);
                  break;
                case 'roll':
                  drawRoll(widget);
                  break;
                case 'stateText':
                  drawStateText(widget);
                  break;
              }

              ctx.restore();
            }

            function drawStick(widget) {
              const radius = Math.max((widget.size || 220) / 2, 12);
              ctx.lineWidth = Math.max(0, widget.lineThickness ?? 3);
              ctx.strokeStyle = color(widget.frameDisplayColor);
              ctx.beginPath();
              ctx.arc(0, 0, radius, 0, Math.PI * 2);
              ctx.stroke();

              ctx.strokeStyle = color(widget.frameDisplayColor, 0.35);
              ctx.lineWidth = Math.max(0, (widget.lineThickness ?? 3) * 0.66);
              ctx.beginPath();
              ctx.moveTo(-radius, 0);
              ctx.lineTo(radius, 0);
              ctx.moveTo(0, -radius);
              ctx.lineTo(0, radius);
              ctx.stroke();

              const x = (widget.xValue || 0) * radius;
              const y = -(widget.yValue || 0) * radius;
              const distance = Math.hypot(x, y);
              const pillWidth = 20 + (widget.activity || 0) * 10;
              const pillLength = Math.max(pillWidth, distance + pillWidth);
              const angle = distance <= 0.01 ? 0 : Math.atan2(y, x);
              ctx.save();
              ctx.rotate(angle);
              ctx.fillStyle = color(widget.displayColor, 0.86);
              roundedRect(-pillWidth / 2, -pillWidth / 2, pillLength, pillWidth, pillWidth / 2);
              ctx.fill();
              ctx.restore();
            }

            function drawThrottle(widget) {
              const w = Math.max(widget.width || 45, 16);
              const h = Math.max(widget.height || 130, 32);
              const radius = Math.max(widget.cornerRadius ?? 8, 0);
              ctx.lineWidth = Math.max(0, widget.lineThickness ?? 3);
              ctx.strokeStyle = color(widget.frameDisplayColor);
              roundedRect(-w / 2, -h / 2, w, h, radius);
              ctx.stroke();

              const ratio = Math.max(0, Math.min(1, widget.fillRatio ?? 0.5));
              const inset = Math.max(4, (widget.lineThickness ?? 3) + 2);
              const innerW = Math.max(w - (inset * 2), 1);
              const innerH = Math.max(h - (inset * 2), 1);
              const centerBand = Math.min(innerH, Math.max(4, (widget.lineThickness ?? 3) + 1));
              const travel = Math.max((innerH - centerBand) / 2, 0);
              const extension = travel * ratio;
              const fillHeight = centerBand + extension;
              const fillTop = (widget.value ?? 0) >= 0
                ? (-centerBand / 2) - extension
                : -centerBand / 2;
              ctx.fillStyle = color(widget.displayColor, 0.82);
              roundedRect(-w / 2 + inset, fillTop, innerW, fillHeight, Math.max(0, radius - inset));
              ctx.fill();
            }

            function drawRoll(widget) {
              const w = Math.max(widget.width || 162, 80);
              const h = Math.max(widget.height || 112, 60);
              if ((widget.renderMode || 'image') === 'image') {
                drawRollImage(widget, w, h);
                return;
              }

              ctx.strokeStyle = color(widget.frameDisplayColor);
              ctx.lineWidth = Math.max(0, widget.lineThickness ?? 5);
              ctx.beginPath();
              ctx.arc(0, 18, w / 2, Math.PI * 1.08, Math.PI * 1.92);
              ctx.stroke();

              ctx.save();
              ctx.rotate((widget.rotationDegrees || 0) * Math.PI / 180);
              ctx.fillStyle = color(widget.displayColor);
              ctx.beginPath();
              ctx.moveTo(0, -h / 2);
              ctx.lineTo(16, 12);
              ctx.lineTo(0, 2);
              ctx.lineTo(-16, 12);
              ctx.closePath();
              ctx.fill();
              ctx.restore();
            }

            function drawRollImage(widget, w, h) {
              const assetId = widget.assetId || 'roll-indicator-gladius';
              const image = loadRollImage(assetId);
              ctx.save();
              ctx.rotate((widget.rotationDegrees || 0) * Math.PI / 180);
              if (image.complete && image.naturalWidth > 0) {
                const tinted = tintImage(image, Math.max(1, Math.ceil(w)), Math.max(1, Math.ceil(h)), color(widget.displayColor));
                ctx.drawImage(tinted, -w / 2, -h / 2, w, h);
              } else {
                ctx.fillStyle = color(widget.displayColor);
                ctx.strokeStyle = color(widget.frameDisplayColor, 0.28);
                ctx.lineWidth = Math.max(0, widget.lineThickness ?? 3);
                const scale = Math.min(w / 160, h / 160);
                ctx.scale(scale, scale);
                ctx.beginPath();
                ctx.moveTo(0, -62);
                ctx.lineTo(19, -12);
                ctx.lineTo(56, 13);
                ctx.lineTo(18, 19);
                ctx.lineTo(0, 62);
                ctx.lineTo(-18, 19);
                ctx.lineTo(-56, 13);
                ctx.lineTo(-19, -12);
                ctx.closePath();
                ctx.fill();
                if ((widget.lineThickness ?? 3) > 0) {
                  ctx.stroke();
                }
              }
              ctx.restore();
            }

            function loadRollImage(assetId) {
              const id = assetId === 'roll-indicator-arrow' ? assetId : 'roll-indicator-gladius';
              if (!rollImages.has(id)) {
                const image = new Image();
                image.decoding = 'async';
                image.src = `/assets/${id}.png`;
                rollImages.set(id, image);
              }

              return rollImages.get(id);
            }

            function tintImage(image, width, height, fillStyle) {
              const tinted = document.createElement('canvas');
              tinted.width = width;
              tinted.height = height;
              const tintCtx = tinted.getContext('2d');
              tintCtx.drawImage(image, 0, 0, width, height);
              tintCtx.globalCompositeOperation = 'source-in';
              tintCtx.fillStyle = fillStyle;
              tintCtx.fillRect(0, 0, width, height);
              return tinted;
            }

            function drawStateText(widget) {
              const effects = widget.textEffects || {};
              const size = Math.max(widget.fontSize || 34, 8);
              const shake = Math.max(0, Math.min(1, widget.shakeIntensity || 0));
              if (shake > 0) {
                const t = performance.now();
                ctx.translate(Math.sin(t * 0.095) * 1.8 * shake, Math.cos(t * 0.12) * 1.2 * shake);
              }
              ctx.font = `700 ${size}px "Segoe UI", Arial, Helvetica, sans-serif`;
              ctx.textAlign = 'center';
              ctx.textBaseline = 'middle';
              ctx.fillStyle = color(widget.displayColor, 0.55 + (widget.intensity || 0) * 0.45);
              const text = widget.text || '';
              if (effects.backplateEnabled) {
                const metrics = ctx.measureText(text);
                const padding = effects.backplatePadding ?? 10;
                const width = metrics.width + (padding * 2);
                const height = size + (padding * 1.2);
                ctx.save();
                ctx.shadowColor = 'transparent';
                ctx.fillStyle = color(effects.backplateColor, 1);
                roundedRect(-width / 2, -height / 2, width, height, effects.backplateRadius || 0);
                ctx.fill();
              ctx.restore();
            }

              applyShadow(effects);
              if (effects.outlineEnabled) {
                ctx.lineWidth = Math.max(0, effects.outlineWidth || 0) * 2;
                ctx.strokeStyle = color(effects.outlineColor, 1);
                ctx.strokeText(text, 0, 0);
              }

              ctx.fillText(widget.text || '', 0, 0);
            }

            requestAnimationFrame(draw);
          </script>
        </body>
        </html>
        """;
}
