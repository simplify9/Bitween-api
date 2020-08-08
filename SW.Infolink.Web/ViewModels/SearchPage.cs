using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SW.PrimitiveTypes;

namespace SW.Infolink.Web
{
    public class SearchPage<T> : PageModel
    {

        public AlertModel Alert { get; set; }

        [BindProperty]
        public string SearchFor { get; set; }

        public PagerModel Pager { get; set; } = new PagerModel();
        public IEnumerable<T> Data { get; set; }

        [BindProperty]
        public SearchFilterModel SearchFilter { get; set; } = new SearchFilterModel();




        public void OnPostAddFilter()
        {
            SearchFilter.Lines.Add(new SearchFilterModel.SearchLine());
        }

        public void OnPostDeleteFilter()
        {
            SearchFilter.Lines.RemoveAt(SearchFilter.Lines.Count - 1);//(new SearchFilterModel.SearchLine($"f{SearchFilter.Lines.Count}", 0, $"v{SearchFilter.Lines.Count}"));
        }

    }


}
