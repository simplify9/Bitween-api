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

namespace SW.Infolink.Web.Pages.Documents
{
    public class EditModel : PageModel
    {

        [BindProperty]
        public DocumentConfig Document { get; set;}

        [BindProperty]
        public string PromotedProperties { get; set; }


        public int Id { get; set; }
        public IInfolinkClient ApiService { get; }

        public EditModel(IInfolinkClient apiService)
        {
            ApiService = apiService;
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {

            try
            {
            
                Document.PromotedProperties = PromotedProperties.ToDictionary();
                await ApiService.PostAsync($"documents/{id}/update", Document);
                return Redirect("~/Documents");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return Page();
            }          
        }
        public async Task OnGetAsync(int id)
        {
            if (id == 0)
                Document = new DocumentConfig();

            else
            {
                Document = await ApiService.GetAsync<DocumentConfig>($"documents/{id}");
                PromotedProperties = Document.PromotedProperties.FromDictionary();
            }
                

       }

 

    }
}