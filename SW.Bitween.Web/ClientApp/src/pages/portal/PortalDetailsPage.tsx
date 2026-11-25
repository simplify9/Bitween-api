import { PageHeader } from "../../components/layout/PageHeader";
import { YourDetails } from "./YourDetails";

/** What we hold for their company and show them, such as where we deliver to them. */
export function PortalDetailsPage() {
  return (
    <div className="max-w-3xl">
      <PageHeader title="Your details" description="What we hold for your company, such as where we deliver to you." />
      <YourDetails />
    </div>
  );
}
