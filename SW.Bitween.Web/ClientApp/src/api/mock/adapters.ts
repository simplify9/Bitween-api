import type { AdapterInfo } from "../types";

/**
 * The adapter catalog: native adapters (in-process, unversioned) and
 * deployed packages (versioned). Mirrors the real backend's discovery +
 * startup-value metadata; ids match the real adapter naming.
 */
export const ADAPTER_CATALOG: AdapterInfo[] = [
  // ——— receivers ———
  {
    id: "NativeHttpReceiver",
    kind: "receiver",
    label: "HTTP endpoint",
    native: true,
    versions: [],
    props: [
      { key: "url", optional: false, secret: false, description: "Endpoint polled for new documents. Tokens allowed." },
      { key: "method", optional: true, default: "GET", secret: false },
      { key: "headers", optional: true, secret: false, description: "One per line — Name: value." },
      { key: "authHeader", optional: true, secret: true, description: "Authorization header sent with each poll." },
    ],
  },
  {
    id: "NativeRebexFtpReceiver",
    kind: "receiver",
    label: "FTP folder",
    native: true,
    versions: [],
    props: [
      { key: "host", optional: false, secret: false },
      { key: "port", optional: true, default: "21", secret: false },
      { key: "username", optional: false, secret: false },
      { key: "password", optional: false, secret: true },
      { key: "path", optional: true, default: "/", secret: false, description: "Folder watched for new files." },
      { key: "deleteAfterDownload", optional: true, default: "true", secret: false },
    ],
  },
  {
    id: "NativeS3Receiver",
    kind: "receiver",
    label: "Amazon S3 bucket",
    native: true,
    versions: [],
    props: [
      { key: "bucket", optional: false, secret: false },
      { key: "region", optional: false, secret: false },
      { key: "accessKey", optional: false, secret: false },
      { key: "secretKey", optional: false, secret: true },
      { key: "prefix", optional: true, secret: false, description: "Only objects under this key prefix are picked up." },
    ],
  },
  {
    id: "NativeAzureBlobReceiver",
    kind: "receiver",
    label: "Azure Blob container",
    native: true,
    versions: [],
    props: [
      { key: "connectionString", optional: false, secret: true },
      { key: "container", optional: false, secret: false },
      { key: "prefix", optional: true, secret: false },
    ],
  },
  {
    id: "SW.Infolink.Adapters.Receivers.Pop3",
    kind: "receiver",
    label: "POP3 mailbox",
    native: false,
    versions: ["1.2.0", "1.3.1"],
    props: [
      { key: "host", optional: false, secret: false },
      { key: "port", optional: true, default: "995", secret: false },
      { key: "username", optional: false, secret: false },
      { key: "password", optional: false, secret: true },
      { key: "deleteAfterRead", optional: true, default: "true", secret: false },
    ],
  },

  // ——— handlers ———
  {
    id: "NativeHttpHandler",
    kind: "handler",
    label: "HTTP endpoint",
    native: true,
    versions: [],
    props: [
      { key: "url", optional: false, secret: false, description: "Where the document is sent. Tokens allowed." },
      { key: "method", optional: true, default: "POST", secret: false },
      { key: "headers", optional: true, secret: false, description: "One per line — Name: value. Tokens allowed." },
      { key: "authToken", optional: true, secret: true, description: "Bearer token attached to each request." },
    ],
  },
  {
    id: "NativeRebexFtpUploadHandler",
    kind: "handler",
    label: "FTP upload",
    native: true,
    versions: [],
    props: [
      { key: "host", optional: false, secret: false },
      { key: "port", optional: true, default: "21", secret: false },
      { key: "username", optional: false, secret: false },
      { key: "password", optional: false, secret: true },
      { key: "path", optional: true, default: "/", secret: false },
      { key: "fileNameTemplate", optional: true, secret: false, description: "e.g. invoice-{id}.xml" },
    ],
  },
  {
    id: "NativeS3UploadHandler",
    kind: "handler",
    label: "Amazon S3 upload",
    native: true,
    versions: [],
    props: [
      { key: "bucket", optional: false, secret: false },
      { key: "region", optional: false, secret: false },
      { key: "accessKey", optional: false, secret: false },
      { key: "secretKey", optional: false, secret: true },
      { key: "keyTemplate", optional: true, secret: false, description: "Object key pattern, e.g. orders/{date}/{id}.json" },
    ],
  },
  {
    id: "NativeAzureBlobUploadHandler",
    kind: "handler",
    label: "Azure Blob upload",
    native: true,
    versions: [],
    props: [
      { key: "connectionString", optional: false, secret: true },
      { key: "container", optional: false, secret: false },
      { key: "blobNameTemplate", optional: true, secret: false },
    ],
  },
  {
    id: "SW.Infolink.Adapters.Handlers.Smtp",
    kind: "handler",
    label: "Email (SMTP)",
    native: false,
    versions: ["2.0.0", "2.1.4"],
    props: [
      { key: "host", optional: false, secret: false },
      { key: "port", optional: true, default: "587", secret: false },
      { key: "from", optional: false, secret: false },
      { key: "to", optional: false, secret: false, description: "Comma-separated recipients. Tokens allowed." },
      { key: "password", optional: false, secret: true },
      { key: "subjectTemplate", optional: true, secret: false },
    ],
  },

  // ——— mappers ———
  {
    id: "NativeJSONMapper",
    kind: "mapper",
    label: "Visual JSON mapping",
    native: true,
    versions: [],
    // Configured through the mapping editor (deferred in this redesign).
    props: [],
  },
  {
    id: "SW.Infolink.Adapters.Mappers.Liquid",
    kind: "mapper",
    label: "Liquid template",
    native: false,
    versions: ["1.4.0"],
    props: [
      { key: "template", optional: false, secret: false, description: "Liquid template producing the output document." },
    ],
  },
  {
    id: "SW.Infolink.Adapters.Mappers.JsonToDelimited",
    kind: "mapper",
    label: "JSON → delimited file",
    native: false,
    versions: ["1.1.0"],
    props: [
      { key: "columns", optional: false, secret: false, description: "Comma-separated JSON paths, one per output column." },
      { key: "delimiter", optional: true, default: ",", secret: false },
    ],
  },

  // ——— validators ———
  {
    id: "SW.Infolink.Adapters.Validators.JsonSchema",
    kind: "validator",
    label: "JSON schema",
    native: false,
    versions: ["1.0.2"],
    props: [
      { key: "schema", optional: false, secret: false, description: "Documents failing this schema are rejected before mapping." },
    ],
  },
];
