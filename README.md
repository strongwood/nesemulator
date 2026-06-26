# NESEmulator

一个基于 C# / WPF 的 NES 模拟器实验项目，包含 CPU、PPU、APU、内存总线、手柄输入以及若干调试窗口。

An experimental NES emulator built with C# and WPF. The project includes CPU, PPU, APU, memory bus, controller input, and several debugging tools.

## 当前状态

- 以桌面程序方式运行
- 支持加载 `.nes` ROM 文件
- 包含 CPU / PPU / APU 核心实现
- 提供反汇编、PPU 调试、NameTable 调试、按键绑定等界面
- 仓库当前不包含测试 ROM、运行日志和构建产物

## Status

- Runs as a desktop application
- Supports loading `.nes` ROM files
- Includes core CPU / PPU / APU implementations
- Provides disassembly, PPU debug, NameTable debug, and key binding tools
- Test ROMs, runtime logs, and build artifacts are not included in this repository

## 开发环境

- Windows
- .NET 9 SDK

## Development Environment

- Windows
- .NET 9 SDK

## 构建

```bash
dotnet build
```

## Build

```bash
dotnet build
```

## 运行

```bash
dotnet run
```

也可以在启动时传入 ROM 路径：

```bash
dotnet run -- "path/to/game.nes"
```

## Run

```bash
dotnet run
```

You can also pass a ROM path at startup:

```bash
dotnet run -- "path/to/game.nes"
```

## 项目结构

- `Core/`：模拟器核心实现
- `Core/CPU/`：6502 CPU
- `Core/PPU/`：2C02 PPU
- `Core/APU/`：2A03 APU
- `Core/Memory/`：内存总线
- `Core/Cartridge/`：卡带与 Mapper
- `Core/Input/`：手柄和键位映射
- `Core/Testing/`：测试运行器

## Project Structure

- `Core/`: emulator core implementation
- `Core/CPU/`: 6502 CPU
- `Core/PPU/`: 2C02 PPU
- `Core/APU/`: 2A03 APU
- `Core/Memory/`: memory bus
- `Core/Cartridge/`: cartridge and mapper support
- `Core/Input/`: controller and key mapping
- `Core/Testing/`: test runners

## 说明

- 本仓库仅保留源码和工程文件，ROM 资源需自行准备
- 运行程序后可能会在输出目录生成日志和临时文件，这些文件已通过 `.gitignore` 忽略
- 项目目前主要面向开发和研究用途

## Notes

- This repository only keeps source code and project files. ROM assets should be prepared separately.
- Running the application may generate logs and temporary files in the output directory. These files are already ignored by `.gitignore`.
- The project is currently intended mainly for development and research purposes.
