'use strict';

/*
 * Lightweight translucent plasma field for Archer's presence orb.
 * Pure browser canvas: no image assets, packages, microphone, telemetry, or API calls.
 * Stops animating offscreen / in hidden tabs and honors reduced-motion preferences.
 */
(() => {
  const stage = document.getElementById('archerPresence');
  const orb = document.getElementById('orbButton');
  const canvas = document.getElementById('orbParticleField');
  if (!stage || !orb || !canvas) return;

  const ctx = canvas.getContext('2d', { alpha: true });
  if (!ctx) return;

  const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
  let width = 0;
  let height = 0;
  let centerX = 0;
  let centerY = 0;
  let orbRadius = 110;
  let activeFrame = 0;
  let lastDraw = 0;
  let inViewport = true;

  // Repeatable, stable positions avoid popping when state changes or viewport resizes.
  let seed = 41041;
  const random = () => {
    seed = (1664525 * seed + 1013904223) >>> 0;
    return seed / 4294967296;
  };
  const particles = Array.from({ length: 76 }, (_, i) => ({
    angle: random() * Math.PI * 2,
    radius: (i < 18 ? .33 + random() * .55 : .95 + random() * .62),
    drift: .46 + random() * .65,
    size: .6 + random() * 1.45,
    opacity: .30 + random() * .48,
    offset: random() * Math.PI * 2,
    direction: random() > .20 ? 1 : -1,
    stretch: .72 + random() * .17
  }));
  const arcSpecs = Array.from({ length: 7 }, () => ({
    angle: random() * Math.PI * 2,
    radius: 1.05 + random() * .49,
    width: .09 + random() * .33,
    frequency: .27 + random() * .55,
    phase: random() * Math.PI * 2
  }));

  const presets = {
    idle: { speed: .18, alpha: .70, color: [101, 226, 220], arcs: 3 },
    listening: { speed: .39, alpha: 1.0, color: [108, 249, 214], arcs: 5 },
    thinking: { speed: .65, alpha: .95, color: [111, 201, 255], arcs: 7 },
    responding: { speed: .33, alpha: 1.08, color: [139, 251, 235], arcs: 5 },
    speaking: { speed: .43, alpha: 1.0, color: [130, 241, 241], arcs: 6 },
    alert: { speed: .36, alpha: .86, color: [240, 186, 120], arcs: 5 },
    error: { speed: .23, alpha: .65, color: [237, 141, 148], arcs: 2 }
  };

  const rgba = (color, alpha) =>
    'rgba(' + color[0] + ',' + color[1] + ',' + color[2] + ',' + alpha + ')';

  function measure() {
    const bounds = canvas.getBoundingClientRect();
    const ball = orb.getBoundingClientRect();
    if (bounds.width <= 0 || bounds.height <= 0) return;
    width = bounds.width;
    height = bounds.height;
    centerX = ball.left + ball.width / 2 - bounds.left;
    centerY = ball.top + ball.height / 2 - bounds.top;
    orbRadius = ball.width / 2;
    const density = Math.min(window.devicePixelRatio || 1, 2);
    const pixelWidth = Math.round(width * density);
    const pixelHeight = Math.round(height * density);
    if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
      canvas.width = pixelWidth;
      canvas.height = pixelHeight;
    }
    ctx.setTransform(density, 0, 0, density, 0, 0);
    draw(lastDraw ? lastDraw : 0, motion.matches);
  }

  function draw(timestamp, still = false) {
    if (!width || !height) return;
    ctx.clearRect(0, 0, width, height);

    const state = presets[stage.dataset.state] || presets.idle;
    const time = still ? 0 : timestamp / 1000;
    const color = state.color;
    const intensity = Math.min(state.alpha, 1.15);
    ctx.globalCompositeOperation = 'lighter';
    ctx.lineCap = 'round';

    // Fine, interrupted energy filaments that follow curved orbital paths.
    // These intentionally avoid sharp zigzag lightning or full continuous rings.
    for (let i = 0; i < state.arcs; i++) {
      const arc = arcSpecs[i];
      const angle = arc.angle + time * arc.frequency * state.speed;
      const stretch = 1 + .025 * Math.sin(time * .52 + arc.phase);
      const rad = orbRadius * arc.radius * stretch;
      const glow = (.10 + .075 * Math.sin(time * .73 + arc.phase) ** 2) * intensity;
      ctx.beginPath();
      ctx.ellipse(centerX, centerY, rad, rad * .84, -.23, angle, angle + arc.width);
      ctx.lineWidth = i < 2 ? 1.05 : .62;
      ctx.strokeStyle = rgba(color, glow);
      ctx.shadowColor = rgba(color, .46);
      ctx.shadowBlur = i < 3 ? 11 : 5;
      ctx.stroke();

      // A subtle second filament nearby adds depth without strobing.
      if (i % 2 === 0) {
        ctx.beginPath();
        ctx.ellipse(centerX, centerY, rad * .98, rad * .82, -.23,
          angle + .045, angle + arc.width * .78);
        ctx.lineWidth = .47;
        ctx.strokeStyle = rgba(color, glow * .42);
        ctx.shadowBlur = 3;
        ctx.stroke();
      }
    }

    // A sparse interior field, plus brighter motes drifting just outside
    // the translucent sphere. No random respawning or abrupt flashes.
    for (let i = 0; i < particles.length; i++) {
      const p = particles[i];
      const phase = p.angle + time * state.speed * p.drift * p.direction;
      const breathing = 1 + .032 * Math.sin(time * .8 + p.offset);
      const listeningPull = stage.dataset.state === 'listening' ? .87 : 1;
      const rad = orbRadius * p.radius * breathing * listeningPull;
      const x = centerX + Math.cos(phase) * rad;
      const y = centerY + Math.sin(phase) * rad * p.stretch;
      const shimmer = .67 + .33 * Math.sin(time * .65 + p.offset) ** 2;
      const alpha = Math.min(.80, p.opacity * shimmer * intensity);
      const size = p.size * (i % 9 === 0 ? 1.32 : 1);

      if (p.radius > .91 && !still) {
        const trailLength = state.speed * (.9 + p.size) * 4;
        ctx.beginPath();
        ctx.moveTo(x - Math.cos(phase + Math.PI / 2) * trailLength,
          y - Math.sin(phase + Math.PI / 2) * trailLength * p.stretch);
        ctx.lineTo(x, y);
        ctx.lineWidth = .5;
        ctx.shadowBlur = 0;
        ctx.strokeStyle = rgba(color, alpha * .42);
        ctx.stroke();
      }

      ctx.beginPath();
      ctx.arc(x, y, size, 0, Math.PI * 2);
      ctx.fillStyle = rgba(color, alpha);
      ctx.shadowColor = rgba(color, alpha * .65);
      ctx.shadowBlur = i % 8 === 0 ? 11 : 4;
      ctx.fill();
    }
    ctx.shadowBlur = 0;
    ctx.globalCompositeOperation = 'source-over';
  }

  function frame(timestamp) {
    activeFrame = 0;
    if (!shouldAnimate()) return;
    // Limit to ~30fps. A beautiful idle orb shouldn't burn a laptop battery.
    if (timestamp - lastDraw >= 32) {
      lastDraw = timestamp;
      draw(timestamp);
    }
    activeFrame = window.requestAnimationFrame(frame);
  }

  function shouldAnimate() {
    return !motion.matches && !document.hidden && inViewport &&
      !stage.closest('.hidden') && width > 0 && height > 0;
  }

  function updatePlayback() {
    if (activeFrame) {
      window.cancelAnimationFrame(activeFrame);
      activeFrame = 0;
    }
    measure();
    if (shouldAnimate()) {
      lastDraw = 0;
      activeFrame = window.requestAnimationFrame(frame);
    } else if (motion.matches) {
      draw(0, true);
    }
  }

  if ('ResizeObserver' in window) {
    new ResizeObserver(measure).observe(stage);
  } else {
    window.addEventListener('resize', measure);
  }

  if ('IntersectionObserver' in window) {
    new IntersectionObserver(entries => {
      inViewport = entries[0]?.isIntersecting ?? false;
      updatePlayback();
    }, { threshold: .04 }).observe(stage);
  }

  new MutationObserver(updatePlayback).observe(stage, {
    attributes: true,
    attributeFilter: ['data-state', 'class']
  });
  document.addEventListener('visibilitychange', updatePlayback);
  if (motion.addEventListener) motion.addEventListener('change', updatePlayback);
  else motion.addListener(updatePlayback);

  updatePlayback();
})();