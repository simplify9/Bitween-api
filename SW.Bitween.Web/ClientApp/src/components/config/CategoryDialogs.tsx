import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil, Trash2 } from "lucide-react";
import { api, type SubscriptionCategory } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { Button, FormError, LoadError, LoadingBlock } from "../ui/basics";
import { Field, TextInput } from "../ui/forms";
import { Dialog } from "../ui/overlays";
import { useCategories } from "./categories";

/** A category's code and description, for a new one or an existing one. */
export function CategoryDialog({
  category,
  onClose,
  onSaved,
}: {
  category?: SubscriptionCategory;
  onClose: () => void;
  onSaved?: (id: number) => void;
}) {
  const queryClient = useQueryClient();
  const [code, setCode] = useState(category?.code ?? "");
  const [description, setDescription] = useState(category?.description ?? "");
  const save = useMutation({
    mutationFn: async () => {
      const input = { code: code.trim(), description: description.trim() };
      if (category) {
        await api.updateCategory(category.id, input);
        return category.id;
      }
      return api.createCategory(input);
    },
    onSuccess: async (id) => {
      await queryClient.invalidateQueries({ queryKey: keys.categories });
      onSaved?.(id);
      onClose();
    },
  });

  return (
    <Dialog title={category ? `Rename ${category.code}` : "New category"} onClose={onClose}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        <Field label="Code" hint="Short, to find subscriptions by: FINANCE, EU-CARRIERS." htmlFor="category-code">
          <TextInput id="category-code" value={code} onChange={(e) => setCode(e.target.value)} autoFocus />
        </Field>
        <Field label="Description" htmlFor="category-description">
          <TextInput id="category-description" value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <FormError>{save.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="primary" type="submit" busy={save.isPending} disabled={!code.trim()}>
            {category ? "Save" : "Create"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

/** Every category, to add, rename and delete. A category in use can't be deleted. */
export function ManageCategoriesDialog({ onClose, usage }: { onClose: () => void; usage: Map<number, number> }) {
  const categories = useCategories();
  const queryClient = useQueryClient();
  const canCreate = useSessionCan("subscriptions.create");
  const canEdit = useSessionCan("subscriptions.edit");
  const canDelete = useSessionCan("subscriptions.delete");
  const [editing, setEditing] = useState<SubscriptionCategory | "new" | null>(null);
  const remove = useMutation({
    mutationFn: (id: number) => api.deleteCategory(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.categories }),
  });

  return (
    <Dialog title="Subscription categories" onClose={onClose}>
      <p className="mb-3 text-[13px] text-ink-500">
        Labels to file subscriptions under and filter the list by. A subscription has at most one.
      </p>
      {categories.isPending ? (
        <LoadingBlock label="Loading categories…" />
      ) : categories.isError ? (
        <LoadError error={categories.error} what="the categories" onRetry={() => void categories.refetch()} />
      ) : categories.data.length === 0 ? (
        <p className="py-4 text-center text-[13px] text-ink-500">None yet.</p>
      ) : (
        <ul className="divide-y divide-ink-100 rounded-lg border border-ink-200">
          {categories.data.map((c) => {
            const used = usage.get(c.id) ?? 0;
            return (
              <li key={c.id} className="flex items-center justify-between gap-3 px-3 py-2">
                <span className="min-w-0">
                  <span className="font-medium text-ink-900">{c.code}</span>
                  {c.description && <span className="block truncate text-[12px] text-ink-500">{c.description}</span>}
                  <span className="block text-[12px] text-ink-500">
                    {used === 0 ? "No subscriptions" : `${used} subscription${used === 1 ? "" : "s"}`}
                  </span>
                </span>
                <span className="flex shrink-0 gap-1">
                  {canEdit && (
                    <Button size="sm" variant="ghost" aria-label={`Rename ${c.code}`} onClick={() => setEditing(c)}>
                      <Pencil className="size-3.5" aria-hidden />
                    </Button>
                  )}
                  {canDelete && (
                    <Button
                      size="sm"
                      variant="ghost"
                      aria-label={`Delete ${c.code}`}
                      disabled={used > 0}
                      title={used > 0 ? "Move its subscriptions to another category first" : undefined}
                      onClick={() => remove.mutate(c.id)}
                    >
                      <Trash2 className="size-3.5" aria-hidden />
                    </Button>
                  )}
                </span>
              </li>
            );
          })}
        </ul>
      )}
      <div className="mt-2">
        <FormError>{remove.error?.message}</FormError>
      </div>
      <div className="mt-4 flex justify-between gap-2">
        {canCreate ? <Button onClick={() => setEditing("new")}>New category</Button> : <span />}
        <Button variant="primary" onClick={onClose}>
          Done
        </Button>
      </div>
      {editing && (
        <CategoryDialog category={editing === "new" ? undefined : editing} onClose={() => setEditing(null)} />
      )}
    </Dialog>
  );
}
