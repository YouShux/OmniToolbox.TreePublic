# 幻化管理手机接口

接口仅在幻化管理模块启用时注册，沿用宿主模块授权。它们是独立 companion IPC，尚未加入宿主 GetInfo 功能注册表；调用前检查 `HasFunction`。模块不开放网络端口，不承担手机连接认证；网络服务端须自行认证、限流并隔离不同用户。

## 推荐异步入口

| 名称 | 参数 | 返回 |
| --- | --- | --- |
| `Omni.Glamour.Management.V2` | JSON 字符串 | `Task<string>` |
| `Omni.Glamour.FrameCache.V2` | `frame:<方案Id>` | `Task<string>` |

管理接口自动转发到 Framework 线程；缓存接口只读取托管缓存，可从后台调用。必须 await，不可在游戏线程同步等待。返回 JSON：

```json
{"schemaVersion":1,"available":true,"error":null,"data":{"ok":true}}
```

`data` 为操作结果，失败时 `data.ok=false` 并提供 `error`。操作可能只排队启动原生预览；成功返回不表示新人物已经完成加载。读取后续帧及状态确认就绪。

```csharp
var gate = pi.GetIpcSubscriber<string, Task<string>>("Omni.Glamour.Management.V2");
if (gate.HasFunction)
{
    string snapshot = await gate.InvokeFunc("{\"action\":\"read\"}");
}
```

## 操作

所有请求含 `action`，方案以稳定 `id` 引用。`read` 返回方案、当前选择、套装及可选资源，`assets:false` 可省略资源。`save`、`delete`、`apply`、`preview-draft` 必须带最近快照的 `revision`；冲突时先刷新，再由用户决定重试。

| action | 其他参数／用途 |
| --- | --- |
| `read` | 可选 `assets` |
| `select` | `id`，选择并启动预览 |
| `search` | `kind` 为 equipment/hair/glasses，`query`，装备还需 `slot` |
| `preview-draft` | `id`、`revision`、`preset`，预览未保存编辑 |
| `save` | `id`、`revision`、`preset`，可选 `gearsetIndex` |
| `delete` / `apply` | `id`、`revision`，删除／场景试穿 |
| `restore` | 撤销场景试穿覆盖 |
| `import` | `text`，模块剪贴板导出内容，创建新方案 |
| `import-website` | `data`，石之家解析结果，含 name、parts；服务端负责页面提取，模块校验并解析物品 |
| `export` | `id`，返回 `text` |
| `camera` | `id`，yaw/x/y/wheel 增量；客户端节流并合并手势 |
| `button` | `id`、`node`，仅接受 32/33/34/35/37/39 原生按钮 |
| `frame` | `id`，读取预览帧及状态 |
| `stop` | 结束手机预览租约；本地窗口仍打开时保留本地预览 |

预设字段和网站 parts 的具体结构以 `GlamourClipboard.cs`、`GlamourRemote.cs` 为准。不要用显示名称代替稳定方案 Id；导入会新建条目且不继承套装绑定。

## 帧传输

先 select，然后循环调用帧缓存入口 `frame:<id>`。结果 data 含 `image`（JPEG data URL）、`serial`、`requestId`、`status`、`error`。相同 serial 可跳过解码；未就绪时 image 可为空。`changed:true` 表示另一端切换了方案，重新获取快照。请求续期预览租约，离开页面发送 stop。一次只保留一个待完成请求，禁止堆积轮询；收到新图后再显示，断线应退避重连。

取帧目标约 30 FPS，CPU 缩放到最高 720 像素高并 JPEG 编码。没有单独启动 GPU 编码器，也不会暂停或重启游戏画面串流。

## 兼容入口

`Omni.Glamour.Management.V1` 和 `Omni.Glamour.FrameCache.V1` 保留 string→string 旧结果，不包 V2 envelope。Management.V1 **只能在 Framework 线程调用**；FrameCache.V1 只读托管缓存。

`Omni.BetterGlamourPreview.*.V1` 为旧进程内预览接口；其中 Frame 事件的原生纹理指针是借用值，只能在同步回调中按图形资源生命周期安全处理，不能跨进程传给手机或保留裸指针异步使用。手机使用上述 JPEG 缓存接口。
