# FileSync

FileSync is a command-line utility built with .NET (C#) for periodically synchronizing files and folders from specified source locations to replica (backup) locations. It operates based on a set of user-defined rules and provides detailed logging of its operations.

## Features

* **Rule-Based Synchronization:** Define multiple source-to-replica path pairings in a simple rules file.
* **Periodic Execution:** Automatically runs synchronization cycles at user-defined intervals.
* **One-Way Sync with Cleanup:**
    * Copies new or modified files from the source to the replica.
    * Creates directory structures in the replica to match the source.
    * Deletes extraneous files and folders in the replica that no longer exist in the source, keeping the replica a mirror of the source.
* **Recursive Synchronization:** Synchronizes entire directory structures.
* **Detailed Logging:** Logs all operations (file copies, deletions, folder creations), warnings, and errors to both the console and a specified overall log file.
* **Per-Rule Cycle Summaries:** After each sync cycle for a rule, a summary is logged, including duration, number of items processed, data transferred, and any errors encountered.
* **Command-Line Interface:** All configurations (rules file path, sync interval, log file path) are provided via command-line arguments.
* **Resilient Operations:** Designed to handle inaccessible paths by logging issues and continuing with other valid rules or operations where possible.
* **Overlap Prevention:** Ensures that a new full synchronization cycle for all rules does not start if a previous one is still in progress.

## Getting Started

### Prerequisites

* .NET Runtime (the version corresponding to how the project was built).

### Execution

To run FileSync, use the following command format in your terminal or command prompt:

```sh
FileSync.exe <RulesFilePath> <IntervalSeconds> <OverallLogFilePath>
