import { useState } from "react";
import { useSessionCan } from "../../../auth/guards";
import { CategoryDialog, useCategories } from "../../../components/config/CategoryDialogs";
import { SearchSelect } from "../../../components/ui/SearchSelect";
import { Fact } from "./Fact";

/** Which category the subscription is filed under, picked like its work group. */
export function CategoryField({
  categoryId,
  onChange,
  canEdit,
  idPrefix,
}: {
  categoryId: number | null;
  onChange: (id: number | null) => void;
  canEdit: boolean;
  idPrefix: string;
}) {
  const categories = useCategories();
  const canCreate = useSessionCan("subscriptions.create");
  const [creating, setCreating] = useState(false);

  return (
    <Fact label="Category">
      <div className="w-52">
        <SearchSelect
          id={`${idPrefix}-category`}
          aria-label="Category"
          value={categoryId === null ? "" : String(categoryId)}
          disabled={!canEdit}
          onChange={(v) => onChange(v === "" ? null : Number(v))}
          clearLabel="None"
          options={(categories.data ?? []).map((c) => ({ value: String(c.id), label: c.code }))}
        />
      </div>
      {canEdit && canCreate && (
        <button
          type="button"
          onClick={() => setCreating(true)}
          className="mt-1 text-[12px] font-medium text-crimson-700 hover:underline"
        >
          + New
        </button>
      )}
      {creating && <CategoryDialog onClose={() => setCreating(false)} onSaved={onChange} />}
    </Fact>
  );
}
