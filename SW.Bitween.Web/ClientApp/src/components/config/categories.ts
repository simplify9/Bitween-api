import { useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";

export const useCategories = () => useQuery({ queryKey: keys.categories, queryFn: () => api.listCategories() });
