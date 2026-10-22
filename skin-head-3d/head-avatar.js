/**
 * 3D 头部头像：只渲染 Minecraft 玩家的头（含帽子层），等距/轻微侧角，输出透明底正方形 PNG。
 * 依赖 @daidr/minecraft-skin-renderer（WebGL2）。
 */
import { use, createSkinViewer } from "@daidr/minecraft-skin-renderer";
import { WebGLRendererPlugin } from "@daidr/minecraft-skin-renderer/webgl";

use(WebGLRendererPlugin);

const NAME_URL = "https://api.mojang.com/users/profiles/minecraft/";
const PROFILE_URL = "https://sessionserver.mojang.com/session/minecraft/profile/";
const PLAYERDB_URL = "https://playerdb.co/api/player/minecraft/";
const ASHCON_URL = "https://api.ashcon.app/mojang/v2/user/";
const CRAFTHEAD_SKIN = "https://crafthead.net/skin/";
const CRAFATAR_SKIN = "https://crafatar.com/skins/";

/** 模型单位：身体原点 y=24，头立方体 offset +4 → 头部中心 y=28；库的相机固定注视 y=16。 */
const HEAD_CENTER_Y = 28;
const CAMERA_TARGET_Y = 16;

/** 相机参数：距离取库允许的最大值 150，配合小 FOV ≈ 正交视角，避免广角畸变。 */
export const HEAD_VIEW = {
  distance: 150,
  fov: 18,
  theta: -0.35, // 弧度，绕 Y 轴：向左约 20°，形成 3/4 侧角
  phi: Math.acos((HEAD_CENTER_Y - CAMERA_TARGET_Y) / 150), // 弧度，约 85.4°，把相机抬到头部高度
};

const STAGE = 1536; // 内部超采样画布，头部约 295px，足够无损缩到 256

/**
 * 玩家名 / UUID → 正版皮肤纹理地址。
 * 优先走 Mojang 官方接口（服务端 / 桌面端没有 CORS 限制时可用）；
 * 官方接口不带 Access-Control-Allow-Origin，浏览器里会被拦，自动降级到带 CORS 的镜像。
 */
export async function resolveSkin(input) {
  const raw = String(input ?? "").trim();
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

/** 官方：name → UUID → 会话资料 → textures.SKIN.url。 */
async function resolveFromMojang(raw) {
  const compact = raw.replace(/-/g, "");
  const uuid = /^[0-9a-f]{32}$/i.test(compact)
    ? compact.toLowerCase()
    : (await fetchJson(NAME_URL + encodeURIComponent(raw), 6000)).id;

  const profile = await fetchJson(PROFILE_URL + uuid, 6000);
  const prop = (profile.properties ?? []).find((p) => p.name === "textures");
  if (!prop) throw new Error("该账号没有皮肤数据");

  const decoded = JSON.parse(new TextDecoder().decode(base64ToBytes(prop.value)));
  const skin = decoded.textures?.SKIN;
  if (!skin?.url) throw new Error("该账号未设置皮肤");

  return {
    uuid,
    name: profile.name,
    skinUrl: skin.url,
    slim: skin.metadata?.model === "slim",
  };
}

/** 镜像：同样来自正版皮肤，但带 CORS 头，浏览器可直接调用。 */
async function resolveFromMirror(raw) {
  const compact = raw.replace(/-/g, "");
  const isUuid = /^[0-9a-f]{32}$/i.test(compact);

  if (isUuid) {
    const uuid = compact.toLowerCase();
    return {
      uuid,
      name: raw,
      skinUrl: CRAFTHEAD_SKIN + uuid,
      slim: await detectSlim(uuid),
    };
  }

  const payload = await fetchJson(PLAYERDB_URL + encodeURIComponent(raw), 8000);
  const player = payload?.data?.player;
  if (!player?.raw_id) throw new Error(`找不到玩家「${raw}」`);
  return {
    uuid: player.raw_id,
    name: player.username,
    skinUrl: player.skin_texture || CRAFTHEAD_SKIN + player.raw_id,
    slim: await detectSlim(player.raw_id),
  };
}

/**
 * 官方「纤细（Slim / Alex）」模型标记只在带 textures 签名的资料里，
 * 镜像接口大多不返回。这里用 Ashcon 兜底，失败就按经典模型处理。
 */
async function detectSlim(nameOrUuid) {
  try {
    const profile = await fetchJson(ASHCON_URL + encodeURIComponent(nameOrUuid), 5000);
    return profile?.textures?.slim === true;
  } catch {
    return false;
  }
}

async function fetchJson(url, timeoutMs) {
  const res = await fetch(url, { signal: AbortSignal.timeout(timeoutMs) });
  if (res.status === 404) throw new Error("找不到该玩家");
  if (!res.ok) throw new Error(`接口返回 ${res.status}`);
  return res.json();
}

function base64ToBytes(value) {
  return Uint8Array.from(atob(value), (c) => c.charCodeAt(0));
}

/** 强制皮肤纹理使用最近邻采样：放大时保持 MC 像素颗粒感，不被线性过滤糊掉。 */
export function forceNearestFilter() {
  if (typeof WebGL2RenderingContext === "undefined") return;
  const proto = WebGL2RenderingContext.prototype;
  if (proto.__nearestPatched) return;
  const original = proto.texParameteri;
  proto.texParameteri = function (target, pname, param) {
    if (pname === this.TEXTURE_MAG_FILTER || pname === this.TEXTURE_MIN_FILTER) {
      param = this.NEAREST;
    }
    return original.call(this, target, pname, param);
  };
  proto.__nearestPatched = true;
}

/** 取皮肤文件；官方纹理被跨域拦截时退回带 CORS 的镜像。 */
async function fetchSkinBlob(url, uuid) {
  const candidates = [url];
  if (uuid) candidates.push(CRAFTHEAD_SKIN + uuid, CRAFATAR_SKIN + uuid);
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

/**
 * 渲染 3D 头部头像到目标 canvas。
 * @param {HTMLCanvasElement} canvas 目标画布（会被设成 size × size）
 * @param {object} options
 * @param {string|Blob} options.skin 皮肤地址或 Blob
 * @param {string} [options.uuid] 皮肤地址失效时的备用来源
 * @param {boolean} [options.slim] 细手臂模型（只影响身体，头部尺寸相同）
 * @param {number} [options.size] 输出边长，默认 256
 */
export async function renderHeadAvatar(canvas, options) {
  const { skin, uuid, slim = false, size = 256 } = options;
  const source = typeof skin === "string" ? await fetchSkinBlob(skin, uuid) : skin;

  // 64×32 旧版皮肤没有帽子层，其 (32,0) 区域在文件里是纯黑像素，
  // 直接渲染外层会得到一个纯黑方块头，因此旧皮肤只画内层。
  const legacy = await isLegacySkin(source);

  forceNearestFilter();

  const stage = document.createElement("canvas");
  stage.width = stage.height = STAGE;

  const viewer = await createSkinViewer({
    canvas: stage,
    skin: source,
    slim,
    preferredBackend: "webgl",
    antialias: true,
    pixelRatio: 1,
    fov: HEAD_VIEW.fov,
    zoom: HEAD_VIEW.distance,
    enableRotate: false,
    enableZoom: false,
    autoRotate: false,
    backEquipment: "none",
  });

  try {
    viewer.resize(STAGE, STAGE);

    // 关键 1：只保留头（含外层帽子），其余部件全部隐藏 → 画布里只剩一个头。
    viewer.setPartsVisibility({
      head: { inner: true, outer: !legacy },
      body: { inner: false, outer: false },
      leftArm: { inner: false, outer: false },
      rightArm: { inner: false, outer: false },
      leftLeg: { inner: false, outer: false },
      rightLeg: { inner: false, outer: false },
    });
    viewer.setBackEquipment("none");

    // 关键 2：相机绕 y=16 的固定注视点旋转，所以先把相机的仰角抬到头部高度，
    // theta 给一点侧角 → 3/4 视角；phi 由”头部中心 - 注视点”的高度差算出来。
    viewer.setRotation(HEAD_VIEW.theta, HEAD_VIEW.phi);
    viewer.setZoom(HEAD_VIEW.distance);

    // 关键 3：按 alpha 包围盒裁切 → 无论视角/朝向如何，输出都只包含头部且居中。
    const shot = viewer.screenshot("png");
    const bounds = await alphaBounds(shot, STAGE);
    if (!bounds) throw new Error("渲染结果为空，可能不支持 WebGL2");

    canvas.width = canvas.height = size;
    const ctx = canvas.getContext("2d");
    ctx.clearRect(0, 0, size, size);
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = "high";

    const image = await loadImage(shot);
    const padding = Math.round(Math.max(bounds.w, bounds.h) * 0.06);
    const side = Math.max(bounds.w, bounds.h) + padding * 2;
    const sx = bounds.x - (side - bounds.w) / 2;
    const sy = bounds.y - (side - bounds.h) / 2;
    // 裁切框超出画布时，用透明像素补齐（drawImage 源矩形会自动裁剪）
    ctx.drawImage(image, sx, sy, side, side, 0, 0, size, size);

    return { width: size, height: size, bounds, dataUrl: canvas.toDataURL("image/png") };
  } finally {
    viewer.dispose();
  }
}

/** 玩家名 / UUID 一步到位。model：auto / classic / slim，用于覆盖自动识别的皮肤形态。 */
export async function renderHeadFromPlayer(canvas, input, size = 256, model = "auto") {
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

/** 旧版皮肤是 2:1（64×32 / 128×64），没有独立的帽子层。 */
async function isLegacySkin(source) {
  try {
    const bitmap = await createImageBitmap(source);
    const legacy = bitmap.width === bitmap.height * 2;
    bitmap.close?.();
    return legacy;
  } catch {
    return false;
  }
}

function loadImage(src) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error("截图解码失败"));
    image.src = src;
  });
}

/** 扫描非透明像素的包围盒。 */
async function alphaBounds(dataUrl, stageSize) {
  const image = await loadImage(dataUrl);
  const c = document.createElement("canvas");
  c.width = c.height = stageSize;
  const ctx = c.getContext("2d", { willReadFrequently: true });
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
