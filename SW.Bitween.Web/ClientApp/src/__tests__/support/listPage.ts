import { http, HttpResponse, type JsonBodyType } from "msw";
import { apiPath } from "./renderApp";

/** A search endpoint's page of rows. */
export const page = (rows: unknown[]) => ({ result: rows, totalCount: rows.length });
export const noRows = page([]);

/** GET `path` answering `body`, and every URL it was asked with, so a test can see what was searched. */
export function recorded(path: string, body: JsonBodyType) {
  const asked: string[] = [];
  const handler = http.get(apiPath(path), ({ request }) => {
    asked.push(decodeURIComponent(request.url));
    return HttpResponse.json(body);
  });
  return { handler, asked };
}

export const empty = (...paths: string[]) => paths.map((p) => http.get(apiPath(p), () => HttpResponse.json(noRows)));
export const none = (...paths: string[]) => paths.map((p) => http.get(apiPath(p), () => HttpResponse.json([])));
