# 安装说明

当前正式安装方案是 `MSI`。

适用对象：
- 产品同学：了解用户怎么安装
- 测试同学：知道安装包位置、首次启动行为、常见问题
- 发布人员：知道如何重新打包

## 给用户

用户安装只需要这几步：

1. 双击 `PhotoSelector.Setup.msi`
2. 选择安装目录
3. 点击下一步并等待安装完成
4. 从桌面或开始菜单打开程序

说明：
- 首次启动时，程序会自动检查依赖
- 系统已有依赖会自动跳过
- 缺少依赖时会联网下载到安装目录内
- 第一次启动稍慢是正常现象

## 安装包位置

打包完成后，安装包在：

```text
dist\msi\PhotoSelector.Setup.msi
```

## 重新打包

在解决方案根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1
```

## 常改配置

文件：`installer/msi.config.props`

可配置内容：
- `ProductName`
- `Manufacturer`
- `ProductVersion`
- `InstallFolderName`

依赖下载地址配置文件：

- `installer/deps.config.json`

## 当前安装链路涉及文件

- `installer/build-msi.ps1`
  - 生成 MSI 安装包
- `installer/msi.config.props`
  - MSI 配置
- `installer/launch.cmd`
  - 安装后启动入口
- `installer/ensure_deps.ps1`
  - 首次启动时检查和安装依赖
- `installer/deps.config.json`
  - 依赖下载地址
- `src/PhotoSelector.Setup/PhotoSelector.Setup.wixproj`
  - MSI 项目
- `src/PhotoSelector.Setup/Package.wxs`
  - MSI 安装定义

## 旧方案说明

旧的 EXE / Inno Setup 方案已归档到：

```text
installer\legacy-inno\
```

这些文件仅保留作历史参考，当前不参与正式打包流程。

## 常见问题

### 安装后打开时会短暂弹出命令行窗口

这是首次检查依赖时的现有行为，不影响正常使用。

### 安装后打开闪退

优先检查：
- 是否使用了最新的 `PhotoSelector.Setup.msi`
- 安装目录是否有读写权限
- 网络是否可访问依赖下载地址

排查重点文件：
- `installer/launch.cmd`
- `installer/ensure_deps.ps1`
- `installer/deps.config.json`
