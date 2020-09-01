using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SW.Infolink;
using SW.Infolink.Model;
using SW.Infolink.Sdk;
using SW.PrimitiveTypes;

namespace SW.Infolink.Web.Pages.Receivers
{
    public class DeleteModel : PageModel
    {
        public IInfolinkClient ApiService { get; }

        public DeleteModel(IInfolinkClient apiService)
        {
            ApiService = apiService;
        }

        public async Task OnGetAsync(int id)
        {

            ViewData["ReceiverName"] = (await ApiService.GetAsync<ReceiverConfig>($"receivers/{id}")).Name;
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            await ApiService.DeleteAsync($"receivers/{id}");
            return Redirect("~/Receivers");
        }

    }
}