# NovelTTS

A powerful, multi-threaded C# WPF application designed to automate the process of crawling web novels, parsing chapters, merging them, and converting the text into high-quality audio files using Text-to-Speech (TTS).

## 🚀 Features

*   **Robust Web Crawling**: Automatically crawls and retrieves chapters from web novel sites (e.g., `truyenfull.today`) efficiently.
*   **Flexible HTML Parsing**: Cleans and extracts core story text from HTML using `HtmlAgilityPack` and `Fizzler`.
*   **Chapter Merging**: Merges multiple individual chapters into larger chunks to facilitate seamless audio listening.
*   **Text-to-Speech (TTS)**: Leverages `System.Speech.Synthesis` with support for built-in or customized installed voices. Outputs to `.wav` or `.mp3` (via `NAudio.Lame`).
*   **Parallel Processing**: Executes TTS jobs and downloads on multiple threads concurrently (adjustable via settings) utilizing `BlockingCollection`.
*   **Resiliency & State Management**: Built-in SQLite database layer (`Entity Framework 6`) retains all tasks. Allows pausing, stopping, and resuming without losing progress. Missing data is easily retried cleanly with `Polly`.
*   **Clean MVVM Architecture**: Separates the UI layer from underlying business logic ensuring a clean and maintanable codebase.

## 🛠️ Technology Stack

*   **.NET Framework**: 4.7.2 (C#)
*   **UI Framework**: Windows Presentation Foundation (WPF) with pure MVVM.
*   **Database**: SQLite + Entity Framework 6 / Dapper (for performance).
*   **Crawling & Parsing**: `HtmlAgilityPack`, custom HTTP Clients with `Polly` for resilient handling.
*   **Audio Engine**: `System.Speech.Synthesis`, `NAudio`, `NAudio.Lame` (WAV to MP3 conversion).
*   **Concurrency**: `System.Threading`, Thread Pools, and `ConcurrentCollections`.

## 📂 Architecture Overview

The project is structured into clear, independent components adhering to SOLID principles:

*   **Models**: Contains core domain entities such as `NovelProject`, `Chapter`, `MergeJob`, `AudioJob` and system representations tracking their status (`Enums`).
*   **ViewModels**: Connects the `MainWindow` view to business services, managing states for Crawler, Merger, and TTS processes (`CrawlerViewModel`, `MergeTtsViewModel`).
*   **Services**:
    *   `Crawler`: Handles requesting, downloading, and storing novel chapters asynchronously.
    *   `Parser`: Sanitizes HTML data to bare readable text.
    *   `Merge`: Combines downloaded text files logically for continuous audiobook blocks.
    *   `TTS`: Converts processed text batches into Audio formats using installed TTS Voices. Provides pause/resume and multi-threading capabilities.
    *   `Project`: Manages novel project contexts.
*   **Data**: The `Repositories` subfolder abstracts `SQLite` transactions for maintaining states robustly if the app is restarted.
*   **Infrastructure**: Centralized `Logging`, Pipeline Coordination, and resilient `HTTP Client` behaviors.

## ⚙️ Getting Started

### Prerequisites

*   Windows OS
*   Visual Studio 2022 (or 2019) with `.NET desktop development` workload installed.
*   Installed Windows TTS Voices (e.g., Microsoft Zira, David, or third-party Vietnamese TTS voices if desired).

### Installation & Run

1. Clone or download the repository.
2. Open `NovelTTS.sln` inside Visual Studio.
3. Restore NuGet packages (usually prompt on build).
4. Set `NovelTTS` as the startup project and Hit `F5` to build and run.

### Usage

1. Open the Application.
2. **Setup Project**: Define a novel project directory and source URL.
3. **Crawl**: Download the chapters of the novel. Progress is saved iteratively into SQLite.
4. **Merge & TTS**: Configure chunk size and desired Voice, then click Run. The application will merge the chapters and synthesize the Audio. You can pause and resume anywhere.

## 📄 License

This project is open-sourced and available under standard terms. Please refer to the `LICENSE` file for details.
