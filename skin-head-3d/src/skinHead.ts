/**
 * 3D 头部头像（TypeScript 版本，供 Vite / Next / 任意打包器使用）
 *
 * 依赖：npm i @daidr/minecraft-skin-renderer
 * 要点：
 *  1. 只保留 head 部件 → 画布里只剩一个头；
 *  2. 相机瞄准点被库固定在 y=16，因此用 phi 把相机抬到头部高度（y=28），theta 给 3/4 侧角；
 *  3. 用 alpha 包围盒裁切 → 输出永远是「只有头 + 居中 + 正方形」，与视角参数无关；
 *  4. 输出透明底 PNG，纹理走最近邻采样保证像素清晰。
 */
import {
  use,
  createSkinViewer,
  type PartsVisibility,
  type SkinViewer,
} from "@daidr/minecraft-skin-renderer";
import { WebGLRendererPlugin } from "@daidr/minecraft-skin-renderer/webgl";

use(WebGLRendererPlugin);

const NAME_URL = "https://api.mojang.com/users/profiles/minecraft/";
const PROFILE_URL = "https://sessionserver.mojang.com/session/minecraft/profile/";
const PLAYERDB_URL = "https://playerdb.co/api/player/minecraft/";
const ASHCON_URL = "https://api.ashcon.app/mojang/v2/user/";
const CRAFTHEAD_SKIN = "https://crafthead.net/skin/";
const CRAFATAR_SKIN = "https://crafatar.com/skins/";

/** 模型单位：身体原点 y=24、头立方体 offset +4 → 头部中心 y=28；库的相机固定注视 y=16。 */
const HEAD_CENTER_Y = 28;
const CAMERA_TARGET_Y = 16;

/** 内部超采样分辨率：头部约 300px，缩到 256 时不会糊。 */
const STAGE = 1536;

export interface SkinProfile {
  uuid: string;
  name: string;
  skinUrl: string;
  slim: boolean;
}

export interface HeadRenderOptions {
  skin: string | Blob;
  /** 主纹理取不到时的备用来源（Crafthead / Crafatar 需要 UUID）。 */
  uuid?: string;
  slim?: boolean;
  /** 输出边长，默认 256。 */
  size?: number;
  /** 水平侧角（弧度），默认 -0.35 ≈ 向左 20°。 */
  theta?: number;
  /** 垂直视场角，越小越接近正交投影。默认 18°。 */
  fov?: number;
  /** 相机距离，库上限 150。默认 150。 */
  distance?: number;
}

export interface HeadBounds {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface HeadRenderResult {
  width: number;
  height: number;
  bounds: HeadBounds;
  dataUrl: string;
}

/** 玩家名 / UUID → 正版皮肤纹理地址。 */
export async function resolveSkin(input: string): Promise<SkinProfile> {
  const raw = input.trim();
  if (!raw) throw new Error("请输入玩家名或 UUID");
  try {
    return await resolveFromMojang(raw);
  } catch (officialError) {
    try {
      return await resolveFromMirror(raw);
    } catch {
      throw officialError;
    }
  }
}

async function resolveFromMojang(raw: string): Promise<SkinProfile> {
  const compact = raw.replace(/-/g, "");
  const uuid = /^[0-9a-f]{32}$/i.test(compact)
    ? compact.toLowerCase()
    : (await fetchJson<{ id: string }>(NAME_URL + encodeURIComponent(raw), 6000)).id;

  const profile = await fetchJson<{
    name: string;
    properties?: { name: string; value: string }[];
  }>(PROFILE_URL + uuid, 6000);

  const prop = (profile.properties ?? []).find((p) => p.name === "textures");
  if (!prop) throw new Error("该账号没有皮肤数据");

  const decoded = JSON.parse(
    new TextDecoder().decode(base64ToBytes(prop.value)),
  ) as {
    textures?: { SKIN?: { url?: string; metadata?: { model?: string } } };
  };

  const skin = decoded.textures?.SKIN;
  if (!skin?.url) throw new Error("该账号未设置皮肤");

  return {
    uuid,
    name: profile.name,
    skinUrl: skin.url,
    slim: skin.metadata?.model === "slim",
  };
}

/** 浏览器里 Mojang 官方接口没有 CORS 头，降级到同样取自正版皮肤的镜像。 */
async function resolveFromMirror(raw: string): Promise<SkinProfile> {
  const compact = raw.replace(/-/g, "");
  if (/^[0-9a-f]{32}$/i.test(compact)) {
    const uuid = compact.toLowerCase();
    return { uuid, name: raw, skinUrl: CRAFTHEAD_SKIN + uuid, slim: await detectSlim(uuid) };
  }

  const payload = await fetchJson<any>(PLAYERDB_URL + encodeURIComponent(raw), 8000);
  const player = payload?.data?.player;
  if (!player?.raw_id) throw new Error(`找不到玩家「${raw}」`);
  return {
    uuid: player.raw_id,
    name: player.username,
    skinUrl: player.skin_texture || CRAFTHEAD_SKIN + player.raw_id,
    slim: await detectSlim(player.raw_id),
  };
}

/** 官方「纤细（Slim / Alex）」标记只在签名资料里，镜像用 Ashcon 兜底。 */
async function detectSlim(nameOrUuid: string): Promise<boolean> {
  try {
    const profile = await fetchJson<any>(ASHCON_URL + encodeURIComponent(nameOrUuid), 5000);
    return profile?.textures?.slim === true;
  } catch {
    return false;
  }
}

async function fetchJson<T>(url: string, timeoutMs: number): Promise<T> {
  const res = await fetch(url, { signal: AbortSignal.timeout(timeoutMs) });
  if (res.status === 404) throw new Error("找不到该玩家");
  if (!res.ok) throw new Error(`接口返回 ${res.status}`);
  return (await res.json()) as T;
}

function base64ToBytes(value: string): Uint8Array {
  return Uint8Array.from(atob(value), (c) => c.charCodeAt(0));
}

/** 皮肤纹理强制最近邻采样：放大时保持 MC 像素颗粒感。 */
export function forceNearestFilter(): void {
  if (typeof WebGL2RenderingContext === "undefined") return;
  const proto = WebGL2RenderingContext.prototype as WebGL2RenderingContext & {
    __nearestPatched?: boolean;
  };
  if (proto.__nearestPatched) return;
  const original = proto.texParameteri;
  proto.texParameteri = function (
    this: WebGL2RenderingContext,
    target: number,
    pname: number,
    param: number,
  ) {
    if (pname === this.TEXTURE_MAG_FILTER || pname === this.TEXTURE_MIN_FILTER) {
      param = this.NEAREST;
    }
    return original.call(this, target, pname, param);
  };
  proto.__nearestPatched = true;
}

/**
 * 渲染 3D 头部头像。
 * @param canvas 目标画布（会被设为 size × size）
 */
export async function renderHeadAvatar(
  canvas: HTMLCanvasElement,
  options: HeadRenderOptions,
): Promise<HeadRenderResult> {
  const {
    skin,
    uuid,
    slim = false,
    size = 256,
    theta = -0.35,
    fov = 18,
    distance = 150,
  } = options;

  const source = typeof skin === "string" ? await fetchSkinBlob(skin, uuid) : skin;
  const legacy = await isLegacySkin(source);

  forceNearestFilter();

  const stage = document.createElement("canvas");
  stage.width = stage.height = STAGE;

  const viewer: SkinViewer = await createSkinViewer({
    canvas: stage,
    skin: source,
    slim,
    preferredBackend: "webgl",
    antialias: true,
    pixelRatio: 1,
    fov,
    zoom: distance,
    enableRotate: false,
    enableZoom: false,
    autoRotate: false,
    backEquipment: "none",
  });

  try {
    viewer.resize(STAGE, STAGE);

    // 只保留头部；旧版 64×32 皮肤没有帽子层，外层是纯黑像素，必须关掉。
    const visibility: PartsVisibility = {
      head: { inner: true, outer: !legacy },
      body: { inner: false, outer: false },
      leftArm: { inner: false, outer: false },
      rightArm: { inner: false, outer: false },
      leftLeg: { inner: false, outer: false },
      rightLeg: { inner: false, outer: false },
    };
    viewer.setPartsVisibility(visibility);
    viewer.setBackEquipment("none");

    // 相机：绕固定注视点 (0,16,0) 抬到头部高度，并给一个轻微侧角。
    const phi = Math.acos((HEAD_CENTER_Y - CAMERA_TARGET_Y) / distance);
    viewer.setRotation(theta, phi);
    viewer.setZoom(distance);

    const shot = viewer.screenshot("png");
    const bounds = await alphaBounds(shot, STAGE);
    if (!bounds) throw new Error("渲染结果为空，浏览器可能不支持 WebGL2");

    canvas.width = canvas.height = size;
    const ctx = canvas.getContext("2d");
    if (!ctx) throw new Error("无法获取 2D 上下文");
    ctx.clearRect(0, 0, size, size);
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = "high";

    const image = await loadImage(shot);
    const padding = Math.round(Math.max(bounds.w, bounds.h) * 0.06);
    const side = Math.max(bounds.w, bounds.h) + padding * 2;
    const sx = bounds.x - (side - bounds.w) / 2;
    const sy = bounds.y - (side - bounds.h) / 2;
    ctx.drawImage(image, sx, sy, side, side, 0, 0, size, size);

    return { width: size, height: size, bounds, dataUrl: canvas.toDataURL("image/png") };
  } finally {
    viewer.dispose();
  }
}

/** 玩家名 / UUID 一步到位。model：auto / classic / slim。 */
export async function renderHeadFromPlayer(
  canvas: HTMLCanvasElement,
  input: string,
  size = 256,
  model: "auto" | "classic" | "slim" = "auto",
): Promise<HeadRenderResult & { profile: SkinProfile }> {
  const profile = await resolveSkin(input);
  const slim = model === "slim" ? true : model === "classic" ? false : profile.slim;
  const result = await renderHeadAvatar(canvas, {
    skin: profile.skinUrl,
    uuid: profile.uuid,
    slim,
    size,
  });
  return { ...result, profile: { ...profile, slim } };
}

async function fetchSkinBlob(url: string, uuid?: string): Promise<Blob> {
  const candidates = [url];
  if (uuid) candidates.push(CRAFTHEAD_SKIN + uuid, CRAFATAR_SKIN + uuid);

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

/** 旧版皮肤是 2:1（64×32 / 128×64），没有独立帽子层。 */
async function isLegacySkin(source: Blob | ImageBitmap): Promise<boolean> {
  try {
    const bitmap = await createImageBitmap(source);
    const legacy = bitmap.width === bitmap.height * 2;
    bitmap.close();
    return legacy;
  } catch {
    return false;
  }
}

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error("截图解码失败"));
    image.src = src;
  });
}

/** 扫描非透明像素包围盒。 */
async function alphaBounds(dataUrl: string, stageSize: number): Promise<HeadBounds | null> {
  const image = await loadImage(dataUrl);
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = stageSize;
  const ctx = canvas.getContext("2d", { willReadFrequently: true });
  if (!ctx) return null;
  ctx.drawImage(image, 0, 0, stageSize, stageSize);

  const { data } = ctx.getImageData(0, 0, stageSize, stageSize);
  let minX = stageSize;
  let minY = stageSize;
  let maxX = -1;
  let maxY = -1;

  for (let y = 0; y < stageSize; y++) {
    for (let x = 0; x < stageSize; x++) {
      if (data[(y * stageSize + x) * 4 + 3] > 8) {
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }
    }
  }
  if (maxX < 0) return null;
  return { x: minX, y: minY, w: maxX - minX + 1, h: maxY - minY + 1 };
}
