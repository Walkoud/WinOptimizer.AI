# WinOptimizer.AI 

**Understand. Monitor. Optimize.**

Welcome to **WinOptimizer.AI**! This tool is designed to help you understand what's happening on your PC and optimize it where it matters. 

⚠️ **Real Talk:** This is **not a magic solution** that will download more RAM or double your FPS instantly. Instead, it's a **powerful diagnostic and management tool** to help you identify and tame apps that are eating up your resources in the background.

<img width="1286" height="893" alt="image" src="https://github.com/user-attachments/assets/dbfeccdd-2092-47bd-a65e-0f5cc95ed193" />


## 🎯 Who is this for?

*   **Low-end / Mid-range PCs**: If every % of CPU or MB of RAM counts for your gaming or work session, this tool is for you.
*   **Gamers**: Automatically kill background processes (like Chrome tabs, updaters) when you launch a game to free up resources.
*   **Debuggers**: Find out exactly which app is causing lag or high usage on your system.
*   *Note: If you have a high-end beast (e.g., i9, 64GB RAM, RTX 4090), you might not feel a huge performance boost, but it remains an excellent monitoring and automation tool!*

## ✨ Key Features

*   **Real-time Monitoring**: Tracks CPU, GPU, RAM, and Network usage per process.
*   **Game Mode**: Automatically terminates background processes that exceed defined thresholds when active.
*   **Smart Alerts**: Interactive Windows notifications allowing you to Kill, Whitelist, or Ignore processes directly.
*   **Process Tree Killing**: Ensures complete termination of multi-process applications (like Browsers).
*   **Optimization Tools**: Integrated access to popular debloating tools (Chris Titus WinUtil, Win11Debloat, etc.).
*   **Low Impact Mode**: Optimized to consume minimal system resources (~0-1% CPU).
*   **Persistent Settings**: All configurations are saved automatically.

## 🤖 How to use the AI Features

WinOptimizer.AI helps you analyze processes, services, and startup apps, but it doesn't have a brain of its own inside the app. It works **with your favorite AI** (ChatGPT, Gemini, Claude, etc.).

1.  **Scan**: Click the scan button in any tab (Tasks, Services, Autoruns).
2.  **Copy Prompt**: Click the **'Copy Prompt'** button. This copies a detailed technical report of your system to your clipboard.
3.  **Ask AI**: Go to ChatGPT, Gemini, or Claude and **PASTE** the prompt.
4.  **Get Recommendations**: The AI will analyze your specific situation and give you a JSON response.
5.  **Apply**: Copy the AI's response and click **'Paste AI Response'** in WinOptimizer.AI. The app will then show you exactly what to disable or optimize based on the AI's advice!

## 📥 Installation

1.  Download the latest release from the [Releases](https://github.com/walkoud/WinOptimizer.AI/releases) page.
2.  Extract the zip file.
3.  Run `WinOptimizer.AI.exe` as **Administrator** (recommended for full process control).

## 🛠️ Building from Source

**Requirements:**
*   Visual Studio 2022 or later
*   .NET 8.0 SDK

**Steps:**
1.  Clone the repository.
2.  Open `src/WinOptimizer.AI.sln` in Visual Studio.
3.  Restore NuGet packages.
4.  Build the solution (Release mode).

## 🤝 Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

## 📄 License

[MIT](LICENSE)
