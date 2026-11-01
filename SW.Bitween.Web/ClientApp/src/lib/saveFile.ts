/**
 * Downloads from a link, the ordinary way: the browser writes it straight to disk as it arrives, so a
 * file of any size is fine. The server names the file.
 */
export function followDownload(url: string): void {
  const a = document.createElement("a");
  a.href = url;
  a.download = "";
  document.body.appendChild(a);
  a.click();
  a.remove();
}
