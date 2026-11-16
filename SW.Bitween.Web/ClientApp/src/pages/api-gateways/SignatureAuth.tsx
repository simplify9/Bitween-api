import { Link } from "react-router";
import type { GatewayAuthentication, GatewaySignatureScheme } from "../../api";
import { Field, Select, TextInput } from "../../components/ui/forms";
import { signatureProblems } from "./signatureProblems";

const SCHEMES: { value: GatewaySignatureScheme; label: string }[] = [
  {
    value: "TimestampedHmacSha256",
    label: "t=…,v1=… (timestamped HMAC-SHA256)",
  },
  { value: "HmacSha256", label: "HMAC-SHA256 of the body" },
];

/** The signature settings, in the Authentication card's grid. */
export function SignatureFields({
  auth,
  canEdit,
  onChange,
}: {
  auth: GatewayAuthentication;
  canEdit: boolean;
  onChange: (auth: GatewayAuthentication) => void;
}) {
  const problems = signatureProblems(auth);
  return (
    <>
      <Field
        label="Signature header"
        htmlFor="ag-sig-header"
        error={problems.signatureHeader}
      >
        <TextInput
          id="ag-sig-header"
          value={auth.signatureHeader}
          disabled={!canEdit}
          className="font-mono"
          placeholder="X-Signature"
          title="The header the caller sends its signature in."
          onChange={(e) =>
            onChange({ ...auth, signatureHeader: e.target.value })
          }
        />
      </Field>
      <Field label="Signature format" htmlFor="ag-sig-scheme">
        <Select
          id="ag-sig-scheme"
          value={auth.signatureScheme}
          disabled={!canEdit}
          options={SCHEMES}
          title="t=…,v1=…: HMAC-SHA256 of {t}.{body}, refused when t is too far from now. HMAC-SHA256 of the body: hex or base64, optionally after sha256=."
          onChange={(e) =>
            onChange({
              ...auth,
              signatureScheme: e.target.value as GatewaySignatureScheme,
            })
          }
        />
      </Field>
      <Field
        label="Secret property"
        htmlFor="ag-sig-secret"
        error={problems.signatureSecretProperty}
      >
        <TextInput
          id="ag-sig-secret"
          value={auth.signatureSecretProperty}
          disabled={!canEdit}
          className="font-mono"
          placeholder="WebhookSecret"
          title="The partner property holding the secret the caller signs with. Mark it secret on the partner."
          onChange={(e) =>
            onChange({ ...auth, signatureSecretProperty: e.target.value })
          }
        />
      </Field>
      {auth.signatureScheme === "TimestampedHmacSha256" && (
        <Field
          label="Tolerance (seconds)"
          htmlFor="ag-sig-tolerance"
          error={problems.signatureToleranceSeconds}
        >
          <TextInput
            id="ag-sig-tolerance"
            type="number"
            value={auth.signatureToleranceSeconds ?? ""}
            disabled={!canEdit}
            placeholder="300"
            title="How far the signed time may be from now. 300 when empty."
            onChange={(e) =>
              onChange({
                ...auth,
                signatureToleranceSeconds:
                  e.target.value === "" ? null : Number(e.target.value),
              })
            }
          />
        </Field>
      )}
    </>
  );
}

/** "How a partner calls it", for a gateway that takes signed calls. */
export function SignedCallHowTo({
  urlName,
  auth,
}: {
  urlName: string;
  auth: GatewayAuthentication;
}) {
  const header = auth.signatureHeader.trim() || "<signature header>";
  const secret = auth.signatureSecretProperty.trim() || "<secret property>";
  const value =
    auth.signatureScheme === "TimestampedHmacSha256"
      ? "t=<unix seconds>,v1=<hex HMAC-SHA256 of {t}.{body}>"
      : "<hex or base64 HMAC-SHA256 of the body>";
  return (
    <>
      <pre className="overflow-x-auto rounded-lg bg-ink-50 px-3 py-2.5 font-mono text-[12px] leading-relaxed text-ink-700">
        {`POST /api/gateway/${urlName}/async\n${header}: ${value}\n\n<the document, as the body>`}
      </pre>
      <p className="mt-2 text-[12px] text-ink-500">
        The signature is what identifies the caller: the exchange runs as the
        attached partner whose{" "}
        <code className="rounded bg-ink-100 px-1 py-0.5 font-mono text-[11px]">
          {secret}
        </code>{" "}
        makes it. Set that property, marked secret, on each partner's{" "}
        <Link to="/partners" className="font-medium link">
          partner page
        </Link>
        . API keys and tokens are refused.
      </p>
    </>
  );
}
