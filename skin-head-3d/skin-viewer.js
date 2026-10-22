/**
 * 3D 皮肤查看器：行走动画 + 拖拽旋转 + 滚轮缩放（@daidr/minecraft-skin-renderer）。
 * 背景透明，尺寸随容器自适应。
 */
import { use, createSkinViewer } from "@daidr/minecraft-skin-renderer";
import { WebGLRendererPlugin } from "@daidr/minecraft-skin-renderer/webgl";
import { resolveSkin } from "./head-avatar.js";

use(WebGLRendererPlugin);

/**
 * 创建并启动一个会走路的 3D 角色。
 * @param {HTMLCanvasElement} canvas
 * @param {object} options
 * @param {string|Blob} options.skin 皮肤地址或 Blob
 * @param {boolean} [options.slim] 细手臂模型
 * @param {number} [options.walkSpeed] 行走速度（1 = 原速）
 * @param {string} [options.uuid] 备用皮肤来源
 */
export async function createWalkingViewer(canvas, options) {
  const { skin, slim = false, walkSpeed = 1, uuid } = options;
  const source = typeof skin === "string" ? await fetchSkinBlob(skin, uuid) : skin;

  const viewer = await createSkinViewer({
    canvas,
    skin: source,
    slim,
    preferredBackend: "webgl",
    antialias: true,
    fov: 45,
    zoom: 62,
    // 需求：旋转和缩放都要开
    enableRotate: true,
    enableZoom: true,
    autoRotate: false,
    backEquipment: "none",
  });

  // 行走动画 + 渲染循环
  viewer.playAnimation("walk", { speed: walkSpeed, amplitude: 1 });
  viewer.startRenderLoop();

  return viewer;
}

/** 切换皮肤：不用重建 viewer，直接 setSkin + setSlim。 */
export async function switchSkin(viewer, input) {
  const profile = await resolveSkin(input);
  const blob = await fetchSkinBlob(profile.skinUrl, profile.uuid);
  await viewer.setSkin(blob);
  viewer.setSlim(profile.slim);
  return profile;
}

async function fetchSkinBlob(url, uuid) {
  const candidates = [url];
  if (uuid) candidates.push(`https://crafthead.net/skin/${uuid}`, `https://crafatar.com/skins/${uuid}`);
  let lastError;
  for (const candidate of candidates) {
    try {
      const res = await fetch(candidate, { mode: "cors" });
      if (!res.ok) throw new Error(String(res.status));
      return await res.blob();
    } catch (error) {
      lastError = error;
    }
  }
  throw new Error("皮肤下载失败：" + (lastError?.message ?? "CORS"));
}

/** 让 canvas 跟随容器尺寸（含 devicePixelRatio）。 */
export function bindResponsive(viewer, canvas) {
  const host = canvas.parentElement;
  if (!host) return () => {};
  const apply = () => {
    const rect = host.getBoundingClientRect();
    if (rect.width < 2 || rect.height < 2) return;
    viewer.resize(rect.width, rect.height);
  };
  apply();
  const observer = new ResizeObserver(apply);
  observer.observe(host);
  return () => observer.disconnect();
}
