/**
 * 3D 皮肤查看器（TypeScript 版本）：行走动画 + 拖拽旋转 + 滚轮缩放。
 * 依赖：npm i @daidr/minecraft-skin-renderer
 */
import { use, createSkinViewer, type SkinViewer } from "@daidr/minecraft-skin-renderer";
import { WebGLRendererPlugin } from "@daidr/minecraft-skin-renderer/webgl";
import { resolveSkin, type SkinProfile } from "./skinHead";

use(WebGLRendererPlugin);

export interface WalkingViewerOptions {
  skin: string | Blob;
  uuid?: string;
  slim?: boolean;
  /** 行走速度，1 = 原速。 */
  walkSpeed?: number;
}

/** 创建并启动一个会走路、可拖拽旋转 / 滚轮缩放的 3D 角色。 */
export async function createWalkingViewer(
  canvas: HTMLCanvasElement,
  options: WalkingViewerOptions,
): Promise<SkinViewer> {
  const { skin, uuid, slim = false, walkSpeed = 1 } = options;
  const source = typeof skin === "string" ? await fetchSkinBlob(skin, uuid) : skin;

  const viewer = await createSkinViewer({
    canvas,
    skin: source,
    slim,
    preferredBackend: "webgl",
    antialias: true,
    fov: 45,
    zoom: 62,
    enableRotate: true,
    enableZoom: true,
    autoRotate: false,
    backEquipment: "none",
  });

  viewer.playAnimation("walk", { speed: walkSpeed, amplitude: 1 });
  viewer.startRenderLoop();
  return viewer;
}

/** 切换皮肤：不用重建 viewer。 */
export async function switchSkin(viewer: SkinViewer, input: string): Promise<SkinProfile> {
  const profile = await resolveSkin(input);
  const blob = await fetchSkinBlob(profile.skinUrl, profile.uuid);
  await viewer.setSkin(blob);
  viewer.setSlim(profile.slim);
  return profile;
}

/** 让 canvas 跟随容器尺寸（含 devicePixelRatio）。 */
export function bindResponsive(viewer: SkinViewer, canvas: HTMLCanvasElement): () => void {
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

async function fetchSkinBlob(url: string, uuid?: string): Promise<Blob> {
  const candidates = [url];
  if (uuid) candidates.push(`https://crafthead.net/skin/${uuid}`, `https://crafatar.com/skins/${uuid}`);

  let lastError: unknown;
  for (const candidate of candidates) {
    try {
      const res = await fetch(candidate, { mode: "cors" });
      if (!res.ok) throw new Error(String(res.status));
      return await res.blob();
    } catch (error) {
      lastError = error;
    }
  }
  throw new Error(`皮肤下载失败：${(lastError as Error)?.message ?? "CORS"}`);
}
