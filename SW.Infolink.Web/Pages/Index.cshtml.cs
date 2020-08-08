using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SW.Infolink;
using SW.Infolink.Sdk;
using SW.PrimitiveTypes;


namespace SW.Infolink.Web.Pages
{
    public class IndexModel : SearchPage<XchangeRow>
    {
        public IInfolinkClient ApiService { get; }

        public IndexModel(IInfolinkClient apiService)
        {
            ApiService = apiService;
        }

        public async Task OnGetRetryAsync(int id)
        {
            await ApiService.PostAsync($"xchanges/{id}/retry", new XchangeRequest());
            await OnPostAsync(0);
        }

        public void OnGet()
        {
            SearchFilter = new SearchFilterModel();
            SearchFilter.Fields.AddRange(new[]
            {
                new SelectListItem("Id", "Id"),
                new SelectListItem("Handler", "HandlerName"),
                new SelectListItem("Subscriber", "SubscriberName"),
                new SelectListItem("Subscriber Id", "SubscriberId"),
                new SelectListItem("Is Successful", "StatusString"),
                //new SelectListItem("Started Date", "StartedOn"),
            });
            SearchFilter.CreateNewName = null;
        }

        public async Task OnPostAsync(int p)
        {
            try
            {
                if (p == 0) p = 1;

                var sreq = new SearchyRequest(SearchFilter.ToSearchyCondition())

                {
                    PageSize = Pager.PageSize,
                    PageIndex = p - 1,
                    Sorts = new List<SearchySort> { new SearchySort("Id", SearchySortOrder.DEC) }
                };

                var sr = await ApiService.GetAsync<SearchyResponse<XchangeRow>>($"xchanges?{sreq}");
                Data = sr.Result;
                Pager.TotalCount = sr.TotalCount;
                Pager.CurrentPage = p;
                if (Pager.TotalCount == 0) Alert = new AlertModel("No data", AlertModel.AlertType.Warning);
            }
            catch (Exception ex)
            {
                Alert = new AlertModel(ex.Message, AlertModel.AlertType.Error);
            }
        }
    }
}