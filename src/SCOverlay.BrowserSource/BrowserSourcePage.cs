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
            }
          ]
        }
        """;

    public const string RollIndicatorSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="-100 -60 200 120">
          <path d="M-78,34 C-43,-22 43,-22 78,34" fill="none" stroke="white" stroke-width="10" stroke-linecap="round" opacity="0.9"/>
          <path d="M0,-34 L18,8 L0,-1 L-18,8 Z" fill="white" opacity="0.95"/>
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
              font-family: Arial, Helvetica, sans-serif;
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
            let latestState = null;
            let lastFetchAt = 0;

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

            function applyShadow(effects) {
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
              ctx.translate(width / 2, height / 2);

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
              const radius = (widget.size || 220) / 2;
              ctx.lineWidth = 3;
              ctx.strokeStyle = color(widget.displayColor);
              ctx.beginPath();
              ctx.arc(0, 0, radius, 0, Math.PI * 2);
              ctx.stroke();

              ctx.strokeStyle = color(widget.ringColor, 0.35);
              ctx.beginPath();
              ctx.moveTo(-radius, 0);
              ctx.lineTo(radius, 0);
              ctx.moveTo(0, -radius);
              ctx.lineTo(0, radius);
              ctx.stroke();

              const x = (widget.xValue || 0) * radius;
              const y = -(widget.yValue || 0) * radius;
              ctx.fillStyle = color(widget.displayColor);
              ctx.beginPath();
              ctx.arc(x, y, 12 + (widget.activity || 0) * 7, 0, Math.PI * 2);
              ctx.fill();
              ctx.lineWidth = 2;
              ctx.beginPath();
              ctx.moveTo(0, 0);
              ctx.lineTo(x, y);
              ctx.strokeStyle = color(widget.displayColor, 0.7);
              ctx.stroke();
            }

            function drawThrottle(widget) {
              const w = widget.width || 45;
              const h = widget.height || 130;
              ctx.lineWidth = 3;
              ctx.strokeStyle = color(widget.displayColor);
              ctx.strokeRect(-w / 2, -h / 2, w, h);

              const ratio = Math.max(0, Math.min(1, widget.fillRatio ?? 0.5));
              const fillHeight = h * ratio;
              ctx.fillStyle = color(widget.displayColor, 0.75);
              ctx.fillRect(-w / 2 + 5, h / 2 - fillHeight + 5, w - 10, Math.max(fillHeight - 10, 0));
            }

            function drawRoll(widget) {
              const w = widget.width || 162;
              const h = widget.height || 112;
              ctx.strokeStyle = color(widget.displayColor);
              ctx.lineWidth = 5;
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

            function drawStateText(widget) {
              const effects = widget.textEffects || {};
              const size = widget.fontSize || 34;
              ctx.font = `700 ${size}px Arial, Helvetica, sans-serif`;
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
