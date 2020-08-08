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


namespace SW.Infolink.Web.Pages.Subscribers
{
    public class IndexModel : SearchPage<SubscriberRow>
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
                new SelectListItem("Id", "Id"),
                new SelectListItem("Handler","HandlerName"),
                new SelectListItem("Mapper","MapperName"),
                new SelectListItem("Document Name","DocumentName"),
                new SelectListItem("Name","Name")



            });
        }

        public async Task OnPostAsync(int p)
        {
            try
            {
                if (p == 0) p = 1;

                var sreq = new SearchyRequest(SearchFilter.ToSearchyCondition())
                {
                    PageSize = Pager.PageSize,
                    PageIndex = p - 1
                };

                var sr = await ApiService.GetAsync<SearchyResponse<SubscriberRow>>($"subscribers?{sreq}");
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