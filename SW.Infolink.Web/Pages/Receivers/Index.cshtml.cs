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


namespace SW.Infolink.Web.Pages.Receivers
{
    public class IndexModel : SearchPage<ReceiverRow>
    {
        public IInfolinkClient ApiService { get; }

        public IndexModel(IInfolinkClient apiService)
        {
            ApiService = apiService;
        }

        public void OnGet()
        {
            SearchFilter = new SearchFilterModel();
            SearchFilter.Fields.AddRange(new[]
            {
                new SelectListItem("Subscriber Id", "Id"),
                new SelectListItem("Adapter Name", "AdapterName"),
                new SelectListItem("Name","Name")

            });
        }

        public async Task OnPostAsync(int p)
        {
            try
            {
                var sreq = new SearchyRequest(SearchFilter.ToSearchyCondition())
                {
                    PageSize = Pager.PageSize,
                    PageIndex = p - 1
                };

                var sr = await ApiService.GetAsync<SearchyResponse<ReceiverRow>>($"receivers?{sreq}");
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