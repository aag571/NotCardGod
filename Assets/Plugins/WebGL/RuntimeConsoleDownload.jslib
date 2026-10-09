mergeInto(LibraryManager.library, {
    RuntimeConsoleOverlay_DownloadTextFile: function (fileNamePtr, contentPtr) {
        var fileName = UTF8ToString(fileNamePtr);
        var content = UTF8ToString(contentPtr);
        var blob = new Blob([content], { type: "text/plain;charset=utf-8" });
        var downloadUrl = URL.createObjectURL(blob);
        var link = document.createElement("a");

        link.href = downloadUrl;
        link.download = fileName;
        link.style.display = "none";
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);

        window.setTimeout(function () {
            URL.revokeObjectURL(downloadUrl);
        }, 1000);
    },

    RuntimeConsoleOverlay_WriteBrowserConsole: function (messagePtr, stackTracePtr, type) {
        var message = UTF8ToString(messagePtr);
        var stackTrace = UTF8ToString(stackTracePtr);
        var output = stackTrace ? message + "\n" + stackTrace : message;

        if (type === 0 || type === 1 || type === 4) {
            console.error(output);
        } else if (type === 2) {
            console.warn(output);
        } else {
            console.log(output);
        }
    }
});
