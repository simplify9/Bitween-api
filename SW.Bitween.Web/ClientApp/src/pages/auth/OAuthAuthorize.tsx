import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import { Bot } from "lucide-react";
import { answerOAuthSignIn, describeOAuthClient } from "../../api";
import { LoadingBlock } from "../../components/ui/basics";
import { AuthLayout } from "./AuthLayout";
import { SignInConsent } from "./SignInConsent";

/**
 * Where an AI app sends the member to connect it to Bitween's MCP server (OAuth's authorization
 * endpoint). The member is signed in here already, however they signed in; approving sends them
 * back to the app with a one-time code only that app can redeem, for access at /mcp alone. The app
 * and the address it sends them back to are checked by the server before anything is shown.
 */
export function OAuthAuthorizePage() {
  const [params] = useSearchParams();
  const clientId = params.get("client_id") ?? "";
  const redirectUri = params.get("redirect_uri") ?? "";
  const codeChallenge = params.get("code_challenge") ?? "";
  const codeChallengeMethod = params.get("code_challenge_method") ?? "";
  const supported = params.get("response_type") === "code" && codeChallengeMethod === "S256" && !!clientId && !!redirectUri;

  const client = useQuery({
    queryKey: ["oauth-client", clientId, redirectUri],
    queryFn: () => describeOAuthClient(clientId, redirectUri),
    enabled: supported,
    retry: false,
  });
  const [leaving, setLeaving] = useState<"approved" | "declined" | null>(null);

  const answer = useMutation({
    mutationFn: (approve: boolean) =>
      answerOAuthSignIn({
        clientId,
        redirectUri,
        codeChallenge,
        codeChallengeMethod,
        resource: params.get("resource"),
        state: params.get("state"),
        approve,
      }),
    onSuccess: ({ redirectTo }, approve) => {
      setLeaving(approve ? "approved" : "declined");
      window.location.assign(redirectTo);
    },
  });

  const name = client.data?.name ?? "An AI app";
  return (
    <AuthLayout>
      <div className="flex items-center gap-3">
        <span className="flex size-10 items-center justify-center rounded-full bg-ink-100 text-ink-600">
          <Bot className="size-5" />
        </span>
        <h1 className="text-2xl font-semibold tracking-tight text-ink-900">Connect {client.data ? name : "an AI app"}</h1>
      </div>

      {!supported ? (
        <p className="mt-4 text-sm text-ink-600">This isn't a sign-in request Bitween can answer. Connect the app again.</p>
      ) : client.isLoading ? (
        <LoadingBlock label="Checking the app…" />
      ) : client.error ? (
        <p className="mt-4 text-sm text-ink-600">{client.error.message}</p>
      ) : leaving ? (
        <p className="mt-4 text-sm text-ink-600">
          {leaving === "approved" ? `Handing the sign-in back to ${name}…` : `Cancelled. ${name} wasn't connected.`}
        </p>
      ) : (
        <SignInConsent
          warning={
            client.data?.onlyThisComputer
              ? "It sends you back to a program on this computer. Only continue if you just connected an AI app here."
              : `Only continue if you just connected ${name} to Bitween.`
          }
          approveLabel="Allow"
          busy={answer.isPending}
          error={answer.error?.message}
          onApprove={() => answer.mutate(true)}
          onCancel={() => answer.mutate(false)}
        >
          <p>
            <span className="font-medium text-ink-900">{name}</span> is asking to use this Bitween as you. It will be
            able to do what your role allows - search exchanges and read their files, retry them, pause subscriptions -
            until you disconnect it on your profile or it goes unused for 30 days.
          </p>
          <p>
            Allowing sends you back to <span className="font-mono text-ink-900">{client.data?.redirectHost}</span>.
          </p>
        </SignInConsent>
      )}
    </AuthLayout>
  );
}
