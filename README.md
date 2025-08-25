# 🧵 Thread Priority Manager

![Screenshot](<img width="982" height="672" alt="image" src="https://github.com/user-attachments/assets/36f58432-29a9-44c6-a4d1-eeb6c2ef602e" />)

A simple **Windows process and thread priority manager** built with **C# (.NET 8, WinForms)**.  
It allows you to list system processes, inspect individual threads, and apply custom priorities.

---

## ⚡ Features
- Real‑time process listing
- Display of threads for the selected process
- Change **process priorities**
- Change **thread priorities** (single thread or all threads)
- Auto‑lock mechanism to keep priorities enforced at a chosen interval
- Clean and responsive WinForms UI

---

## 🛠️ Technologies
- [.NET 8.0](https://dotnet.microsoft.com/)  
- Windows Forms (WinForms)  
- Native Windows API (P/Invoke)

---

## ▶️ Build Instructions
### Requirements
- [SDK .NET 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)  
- Windows 10/11  
- Visual Studio 2022 **or** just the `dotnet CLI`

### Steps
```bash
git clone https://github.com/YOUR_USERNAME/ThreadPriorityManager.git
cd ThreadPriorityManager
dotnet build
