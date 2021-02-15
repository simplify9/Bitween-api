function saveFile(filename, bytesBase64) {
    var link = document.createElement('a');
    link.download = `${filename}.txt`;
    link.href = "data:application/octet-stream;base64," + bytesBase64;
    console.log(link);
    document.body.appendChild(link); // Needed for Firefox
    link.click();
    document.body.removeChild(link);
}