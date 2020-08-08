using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SW.Infolink;
using SW.Infolink.Sdk;
using SW.PrimitiveTypes;

namespace SW.Infolink.Web.Pages.AccessKeySets
{
    public class EditModel : PageModel
    {

        public int Id { get; set; }

        [BindProperty]
        public AccessKeySetConfig AccessKeySet { get; set; }


        public IInfolinkClient ApiService { get; }

        public EditModel(IInfolinkClient apiService)
        {
            ApiService = apiService;
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            try
            {

                if (id != 0)
                {
                    await ApiService.PostAsync($"accesskeysets/{id}/update", AccessKeySet);
                }
                else
                {
                   id= await ApiService.PostAsync<int>($"accesskeysets", AccessKeySet);
                }

                return Redirect("~/AccessKeySets");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return Page();
            }

        }
        public async Task OnGetAsync(int id)
        {
            Id = id;
            AccessKeySet = new AccessKeySetConfig();

            if (id != 0)

                AccessKeySet = await ApiService.GetAsync<AccessKeySetConfig>($"accesskeysets/{id}");
        }
    }
}