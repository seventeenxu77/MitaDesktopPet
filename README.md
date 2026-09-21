# Mita Desktop Pet

一个运行在 Windows 桌面的 Unity 3D 桌宠原型，包含角色拖动、窗口边缘停靠、坐下、鼠标注视、文字对话和本地语音合成。

本仓库只提供桌宠程序的增量内容，不包含原游戏资源、角色模型、语音权重、第三方运行环境或任何账号凭据。使用者必须自行取得并确认拥有相应资源的使用权。

## 系统要求

- Windows 10/11 64 位
- Unity 2021.3 LTS
- 支持该角色的 Unity 基础工程和必要资源
- [Codex CLI](https://developers.openai.com/codex/cli/)
- [GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS)
- 与 GPT-SoVITS 兼容的语音模型和参考音频
- 推荐使用支持 CUDA 的 NVIDIA 显卡

## 获取项目

```powershell
git clone https://github.com/seventeenxu77/MitaDesktopPet.git
cd MitaDesktopPet
```

准备一个兼容的 Unity 基础工程，然后将仓库中的 Unity 内容安装进去：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-UnityOverlay.ps1 `
  -ProjectPath '<Unity 项目目录>'
```

打开 Unity 并等待资源导入完成，然后执行：

```text
Tools > Desktop Pet > 更新桌宠
```

打开生成的桌宠场景即可进入 Play Mode 测试，也可以通过同一菜单中的 Windows 构建命令生成桌宠程序。

## 配置对话

安装 Codex CLI 后执行：

```powershell
codex login
codex login status
```

登录过程会打开浏览器。桌宠启动时会自动连接本机 Codex，使用当前用户自己的 ChatGPT 登录状态；仓库和构建结果都不包含登录凭据。

## 配置本地语音

1. 从官方仓库取得 GPT-SoVITS。
2. 根据官方说明安装 Python、PyTorch 和预训练模型。
3. 使用 `versions.lock.json` 中记录的兼容版本。
4. 应用 `patches` 目录中的 Windows 运行补丁。
5. 将 `gpt-sovits` 目录中的配置模板复制到 GPT-SoVITS，并填写自己的模型路径。
6. 准备兼容的语音模型、参考音频、参考文本和语言设置。
7. 设置 `GPT_SOVITS_HOME` 与 `GPT_SOVITS_PYTHON` 环境变量。

完成配置后，桌宠会自动启动本地语音服务。首次启动需要加载模型，等待时间取决于电脑性能。

## 运行

1. 启动 Unity Play Mode 或 Windows 构建。
2. 点击桌宠的对话入口。
3. 在设置中检查 Codex 登录状态和语音服务状态。
4. 可以先使用“试听”验证本地语音，再发送正式对话。

如果语音功能未配置，可以在设置中关闭语音，其他桌宠功能仍可使用。

### 桌面操作

- 在人物身上按住鼠标右键拖动，松开右键放下；左键用于界面操作。
- 将人物臀部移到窗口上边缘附近后放下，即可吸附坐下。
- 坐稳约 2 秒后，人物会慢慢抬起支撑脚并在半空挪动，较快踩稳后，再缓缓搭成二郎腿。此后持续保持该坐姿，只让脚掌和脚趾轻微活动，不会自动恢复普通坐姿。
- 动作过程中可随时右键拖动，重新坐下后会再次触发。

## 未包含的内容

- 原游戏或其他第三方的模型、贴图、材质、动画和音频
- 自定义语音权重与参考音频
- Unity Editor、Python、PyTorch、CUDA 和 GPT-SoVITS 运行环境
- Codex 登录信息、API Key 和其他个人配置

请遵守 Unity、OpenAI、GPT-SoVITS 以及所用角色和声音资源各自的许可与使用条款。
