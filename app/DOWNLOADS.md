# Download progress

## Chrome

In Iris, open **Settings → Downloads → Connect Chrome**.

1. Open Chrome and select **Extensions → Manage Extensions** in the top-right menu. **Open Chrome Extensions** in Iris is a shortcut to that page.
2. Turn on **Developer mode** in the top right.
3. Select **Copy folder path** in Iris.
4. In Chrome select **Load unpacked**, paste the path into the folder picker's address bar, press Enter, and select **Select Folder**.

If Iris is still waiting, open the extension from Chrome's puzzle-piece menu and select **Connect**. Iris confirms the connection after Chrome responds.

If Iris is already in that Chrome profile, choose **Iris is already in Chrome** in the guide and click the circular **Reload** arrow on the Iris card. The extension and helper use a stable directory under `%LOCALAPPDATA%\Iris\ChromeDownloadBridge`. Active download filenames and byte counts stay on this PC. [Chrome's unpacked extension guide](https://developer.chrome.com/docs/extensions/get-started/tutorial/hello-world#load-unpacked)

Iris can also read recent download records from supported local browser profiles. When the browser has not supplied the total size, Iris shows an indeterminate indicator.

## Steam

Iris reads local Steam transfer activity and available counters from Steam's Downloads view. The read-only helper may get more data while that view is open. When current totals are unavailable, the notch shows an arrow without a percentage. If Chrome and Steam are both downloading, Chrome takes the download slot. Details remain available in Settings.
