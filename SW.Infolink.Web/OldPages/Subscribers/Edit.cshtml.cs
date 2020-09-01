using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SW.Infolink;
using SW.Infolink.Model;
using SW.Infolink.Sdk;
using SW.PrimitiveTypes;

namespace SW.Infolink.Web.Pages.Subscribers
{
    public class EditModel : PageModel
    {

        public int Id { get; set; }
        [BindProperty]
        public SubscriberConfig Subscriber { get; set; }

        [BindProperty]
        public string Properties { get; set; }

        [BindProperty]
        public string Schedules { get; set; }

        [BindProperty]
        public string DocumentFilter { get; set; }
        public IInfolinkClient ApiService { get; }

        public EditModel(IInfolinkClient apiService)
        {
            ApiService = apiService;
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {

            try
            {
                Subscriber.Properties = Properties.ToDictionary();
                Subscriber.Schedules = Schedules.ToSchedules(Subscriber.Schedules);
                Subscriber.DocumentFilter = DocumentFilter.ToDictionary();

                if (id != 0)
                {
                    await ApiService.PostAsync($"subscribers/{id}/update", Subscriber);
                }
                else
                {
                    id = await ApiService.PostAsync<int>($"subscribers", Subscriber);
                }

                return  Redirect("~/Subscribers");
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
            Subscriber = new SubscriberConfig();

            if (id != 0)
            {
                Subscriber = await ApiService.GetAsync<SubscriberConfig>($"subscribers/{id}");
                Properties = Subscriber.Properties.FromDictionary();
                Schedules = Subscriber.Schedules.FromSchedules();
                DocumentFilter = Subscriber.DocumentFilter.FromDictionary();
            }

        }
    }
}