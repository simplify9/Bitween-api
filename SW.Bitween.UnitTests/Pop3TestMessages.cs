namespace SW.Bitween.UnitTests;

internal static class Pop3TestMessages
{
    public const string Plain =
        "From: sender@example.com\r\n" +
        "To: receiver@example.com\r\n" +
        "Subject: Plain Message\r\n" +
        "Date: Mon, 1 Jan 2024 00:00:00 +0000\r\n" +
        "Content-Type: text/plain; charset=utf-8\r\n" +
        "\r\n" +
        "Hello, this is the body text.\r\n";

    // Attachment content ("SGVsbG8gV29ybGQh") base64-decodes to "Hello World!"
    public const string WithAttachment =
        "From: sender@example.com\r\n" +
        "To: receiver@example.com\r\n" +
        "Subject: With Attachment\r\n" +
        "Date: Mon, 1 Jan 2024 00:00:00 +0000\r\n" +
        "MIME-Version: 1.0\r\n" +
        "Content-Type: multipart/mixed; boundary=\"BOUNDARY123\"\r\n" +
        "\r\n" +
        "--BOUNDARY123\r\n" +
        "Content-Type: text/plain; charset=utf-8\r\n" +
        "\r\n" +
        "See attached file.\r\n" +
        "\r\n" +
        "--BOUNDARY123\r\n" +
        "Content-Type: application/octet-stream; name=\"hello.txt\"\r\n" +
        "Content-Transfer-Encoding: base64\r\n" +
        "Content-Disposition: attachment; filename=\"hello.txt\"\r\n" +
        "\r\n" +
        "SGVsbG8gV29ybGQh\r\n" +
        "\r\n" +
        "--BOUNDARY123--\r\n";

    public const string AttachmentDecoded = "Hello World!";
}
