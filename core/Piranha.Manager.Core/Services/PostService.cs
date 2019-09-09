/*
 * Copyright (c) 2019 Håkan Edling
 *
 * This software may be modified and distributed under the terms
 * of the MIT license.  See the LICENSE file for details.
 *
 * https://github.com/piranhacms/piranha.core
 *
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;
using Piranha.Models;
using Piranha.Manager.Models;
using Piranha.Manager.Models.Content;
using Piranha.Services;

namespace Piranha.Manager.Services
{
    public class PostService
    {
        private readonly IApi _api;
        private readonly IContentFactory _factory;

        public PostService(IApi api, IContentFactory factory)
        {
            _api = api;
            _factory = factory;
        }

        public async Task<PostModalModel> GetArchiveMap(Guid? siteId, Guid? archiveId)
        {
            var model = new PostModalModel();

            // Get default site if none is selected
            if (!siteId.HasValue)
            {
                var site = await _api.Sites.GetDefaultAsync();
                if (site != null)
                {
                    siteId = site.Id;
                }
            }

            model.SiteId = siteId.Value;

            // Get the sites available
            model.Sites = (await _api.Sites.GetAllAsync())
                .Select(s => new PostModalModel.SiteItem
                {
                    Id = s.Id,
                    Title = s.Title
                })
                .OrderBy(s => s.Title)
                .ToList();

            // Get the current site title
            var currentSite = model.Sites.FirstOrDefault(s => s.Id == siteId.Value);
            if (currentSite != null)
            {
                model.SiteTitle = currentSite.Title;
            }

            // Get the blogs available
            model.Archives = (await _api.Pages.GetAllBlogsAsync<PageInfo>(siteId.Value))
                .Select(p => new PostModalModel.ArchiveItem
                {
                    Id = p.Id,
                    Title = p.Title,
                    Slug = p.Slug
                })
                .OrderBy(p => p.Title)
                .ToList();

            if (model.Archives.Count() > 0)
            {
                if (!archiveId.HasValue)
                {
                    // Select the first blog
                    archiveId = model.Archives.First().Id;
                }

                var archive = model.Archives.FirstOrDefault(b => b.Id == archiveId.Value);
                if (archive != null)
                {
                    model.ArchiveId = archive.Id;
                    model.ArchiveTitle = archive.Title;
                    model.ArchiveSlug = archive.Slug;
                }

                // Get the available posts
                model.Posts = (await _api.Posts.GetAllAsync<PostInfo>(archiveId.Value))
                    .Select(p => new PostModalModel.PostModalItem
                    {
                        Id = p.Id,
                        Title = p.Title,
                        Permalink = "/" + model.ArchiveSlug + "/" + p.Slug,
                        Published = p.Published.HasValue ? p.Published.Value.ToString("yyyy-MM-dd HH:mm") : null
                    }).ToList();

                // Sort so we show unpublished drafts first
                model.Posts = model.Posts.Where(p => string.IsNullOrEmpty(p.Published))
                    .Concat(model.Posts.Where(p => !string.IsNullOrEmpty(p.Published)))
                    .ToList();
            }

            return model;
        }

        public async Task<PostListModel> GetList(Guid archiveId)
        {
            var model = new PostListModel
            {
                PostTypes = App.PostTypes.Select(t => new PostListModel.PostTypeItem
                {
                    Id = t.Id,
                    Title = t.Title,
                    AddUrl = "manager/post/add/"
                }).ToList()
            };

            // Get posts
            model.Posts = (await _api.Posts.GetAllAsync<PostInfo>(archiveId))
                .Select(p => new PostListModel.PostItem
                {
                    Id = p.Id.ToString(),
                    Title = p.Title,
                    TypeName = model.PostTypes.First(t => t.Id == p.TypeId).Title,
                    Category = p.Category.Title,
                    Published = p.Published.HasValue ? p.Published.Value.ToString("yyyy-MM-dd HH:mm") : null,
                    Status = GetState(p, false),
                    isScheduled = p.Published.HasValue && p.Published.Value > DateTime.Now,
                    EditUrl = "manager/post/edit/"
                }).ToList();

            // Get categories
            model.Categories = (await _api.Posts.GetAllCategoriesAsync(archiveId))
                .Select(c => new PostListModel.CategoryItem
                {
                    Id = c.Id.ToString(),
                    Title = c.Title
                }).ToList();

            return model;
        }

        public async Task<PostEditModel> GetById(Guid id, bool useDraft = true)
        {
            var isDraft = true;
            var post = useDraft ? await _api.Posts.GetDraftByIdAsync(id) : null;

            if (post == null)
            {
                post = await _api.Posts.GetByIdAsync(id);
                isDraft = false;
            }

            if (post != null)
            {
                var postModel =  Transform(post, isDraft);

                postModel.Categories = (await _api.Posts.GetAllCategoriesAsync(post.BlogId))
                    .Select(c => c.Title).ToList();
                postModel.Tags = (await _api.Posts.GetAllTagsAsync(post.BlogId))
                    .Select(t => t.Title).ToList();

                postModel.SelectedCategory = post.Category.Title;
                postModel.SelectedTags = post.Tags.Select(t => t.Title).ToList();

                return postModel;
            }
            return null;
        }

        public async Task<PostEditModel> Create(Guid archiveId, string typeId)
        {
            var post = _api.Posts.Create<DynamicPost>(typeId);

            if (post != null)
            {
                post.Id = Guid.NewGuid();
                post.BlogId = archiveId;

                var postModel = Transform(post, false);

                postModel.Categories = (await _api.Posts.GetAllCategoriesAsync(post.BlogId))
                    .Select(c => c.Title).ToList();
                postModel.Tags = (await _api.Posts.GetAllTagsAsync(post.BlogId))
                    .Select(t => t.Title).ToList();

                postModel.SelectedCategory = post.Category?.Title;
                postModel.SelectedTags = post.Tags.Select(t => t.Title).ToList();

                return postModel;
            }
            return null;
        }

        public async Task Save(PostEditModel model, bool draft)
        {
            var postType = App.PostTypes.GetById(model.TypeId);

            if (postType != null)
            {
                if (model.Id == Guid.Empty)
                {
                    model.Id = Guid.NewGuid();
                }

                var post = await _api.Posts.GetByIdAsync(model.Id);

                if (post == null)
                {
                    post = _factory.Create<DynamicPost>(postType);
                    post.Id = model.Id;
                }

                post.BlogId = model.BlogId;
                post.TypeId = model.TypeId;
                post.Title = model.Title;
                post.Slug = model.Slug;
                post.MetaKeywords = model.MetaKeywords;
                post.MetaDescription = model.MetaDescription;
                post.Published = !string.IsNullOrEmpty(model.Published) ? DateTime.Parse(model.Published) : (DateTime?)null;
                post.RedirectUrl = model.RedirectUrl;
                post.RedirectType = (RedirectType)Enum.Parse(typeof(RedirectType), model.RedirectType);

                // Save category
                post.Category = new Taxonomy
                {
                    Title = model.SelectedCategory
                };

                // Save tags
                post.Tags.Clear();
                foreach (var tag in model.SelectedTags)
                {
                    post.Tags.Add(new Taxonomy
                    {
                        Title = tag
                    });
                }

                // Save regions
                foreach (var region in postType.Regions)
                {
                    var modelRegion = model.Regions
                        .FirstOrDefault(r => r.Meta.Id == region.Id);

                    if (region.Collection)
                    {
                        var listRegion = (IRegionList)((IDictionary<string, object>)post.Regions)[region.Id];

                        listRegion.Clear();

                        foreach (var item in modelRegion.Items)
                        {
                            if (region.Fields.Count == 1)
                            {
                                listRegion.Add(item.Fields[0].Model);
                            }
                            else
                            {
                                var postRegion = new ExpandoObject();

                                foreach (var field in region.Fields)
                                {
                                    var modelField = item.Fields
                                        .FirstOrDefault(f => f.Meta.Id == field.Id);
                                    ((IDictionary<string, object>)postRegion)[field.Id] = modelField.Model;
                                }
                                listRegion.Add(postRegion);
                            }
                        }
                    }
                    else
                    {
                        var postRegion = ((IDictionary<string, object>)post.Regions)[region.Id];

                        if (region.Fields.Count == 1)
                        {
                            ((IDictionary<string, object>)post.Regions)[region.Id] =
                                modelRegion.Items[0].Fields[0].Model;
                        }
                        else
                        {
                            foreach (var field in region.Fields)
                            {
                                var modelField = modelRegion.Items[0].Fields
                                    .FirstOrDefault(f => f.Meta.Id == field.Id);
                                ((IDictionary<string, object>)postRegion)[field.Id] = modelField.Model;
                            }
                        }
                    }
                }

                // Save blocks
                post.Blocks.Clear();

                foreach (var block in model.Blocks)
                {
                    if (block is BlockGroupModel blockGroup)
                    {
                        var groupType = App.Blocks.GetByType(blockGroup.Type);

                        if (groupType != null)
                        {
                            var postBlock = (Extend.BlockGroup)Activator.CreateInstance(groupType.Type);

                            postBlock.Id = blockGroup.Id;
                            postBlock.Type = blockGroup.Type;

                            foreach (var field in blockGroup.Fields)
                            {
                                var prop = postBlock.GetType().GetProperty(field.Meta.Id, App.PropertyBindings);
                                prop.SetValue(postBlock, field.Model);
                            }

                            foreach (var item in blockGroup.Items)
                            {
                                postBlock.Items.Add(item.Model);
                            }
                            post.Blocks.Add(postBlock);
                        }
                    }
                    else if (block is BlockItemModel blockItem)
                    {
                        post.Blocks.Add(blockItem.Model);
                    }
                }

                // Save post
                if (draft)
                {
                    await _api.Posts.SaveDraftAsync(post);
                }
                else
                {
                    await _api.Posts.SaveAsync(post);
                }
            }
            else
            {
                throw new ValidationException("Invalid Post Type.");
            }
        }

        /// <summary>
        /// Deletes the post with the given id.
        /// </summary>
        /// <param name="id">The unique id</param>
        public Task Delete(Guid id)
        {
            return _api.Posts.DeleteAsync(id);
        }

        private PostEditModel Transform(DynamicPost post, bool isDraft)
        {
            var type = App.PostTypes.GetById(post.TypeId);

            var model = new PostEditModel
            {
                Id = post.Id,
                BlogId = post.BlogId,
                TypeId = post.TypeId,
                Title = post.Title,
                Slug = post.Slug,
                MetaKeywords = post.MetaKeywords,
                MetaDescription = post.MetaDescription,
                Published = post.Published.HasValue ? post.Published.Value.ToString("yyyy-MM-dd HH:mm") : null,
                RedirectUrl = post.RedirectUrl,
                RedirectType = post.RedirectType.ToString(),
                State = GetState(post, isDraft),
                UseBlocks = type.UseBlocks
            };

            foreach (var regionType in type.Regions)
            {
                var region = new RegionModel
                {
                    Meta = new RegionMeta
                    {
                        Id = regionType.Id,
                        Name = regionType.Title,
                        Description = regionType.Description,
                        Placeholder = regionType.ListTitlePlaceholder,
                        IsCollection = regionType.Collection,
                        Icon = regionType.Icon,
                        Display = regionType.Display.ToString().ToLower()
                    }
                };
                var regionListModel = ((IDictionary<string, object>)post.Regions)[regionType.Id];

                if (!regionType.Collection)
                {
                    var regionModel = (IRegionList)Activator.CreateInstance(typeof(RegionList<>).MakeGenericType(regionListModel.GetType()));
                    regionModel.Add(regionListModel);
                    regionListModel = regionModel;
                }

                foreach (var regionModel in (IEnumerable)regionListModel)
                {
                    var regionItem = new RegionItemModel();

                    foreach (var fieldType in regionType.Fields)
                    {
                        var appFieldType = App.Fields.GetByType(fieldType.Type);

                        var field = new FieldModel
                        {
                            Meta = new FieldMeta
                            {
                                Id = fieldType.Id,
                                Name = fieldType.Title,
                                Component = appFieldType.Component,
                                Placeholder = fieldType.Placeholder,
                                IsHalfWidth = fieldType.Options.HasFlag(FieldOption.HalfWidth),
                                Description = fieldType.Description
                            }
                        };

                        if (typeof(Extend.Fields.SelectFieldBase).IsAssignableFrom(appFieldType.Type))
                        {
                            foreach(var item in ((Extend.Fields.SelectFieldBase)Activator.CreateInstance(appFieldType.Type)).Items)
                            {
                                field.Meta.Options.Add(Convert.ToInt32(item.Value), item.Title);
                            }
                        }

                        if (regionType.Fields.Count > 1)
                        {
                            field.Model = (Extend.IField)((IDictionary<string, object>)regionModel)[fieldType.Id];

                            if (regionType.ListTitleField == fieldType.Id)
                            {
                                regionItem.Title = field.Model.GetTitle();
                                field.Meta.NotifyChange = true;
                            }
                        }
                        else
                        {
                            field.Model = (Extend.IField)regionModel;
                            field.Meta.NotifyChange = true;
                            regionItem.Title = field.Model.GetTitle();
                        }
                        regionItem.Fields.Add(field);
                    }

                    if (string.IsNullOrWhiteSpace(regionItem.Title))
                    {
                        regionItem.Title = "...";
                    }

                    region.Items.Add(regionItem);
                }
                model.Regions.Add(region);
            }

            foreach (var block in post.Blocks)
            {
                var blockType = App.Blocks.GetByType(block.Type);

                if (block is Extend.BlockGroup)
                {
                    var group = new BlockGroupModel
                    {
                        Id = block.Id,
                        Type = block.Type,
                        Meta = new BlockMeta
                        {
                            Name = blockType.Name,
                            Icon = blockType.Icon,
                            Component = "block-group",
                            IsGroup = true
                        }
                    };

                    if (blockType.Display != BlockDisplayMode.MasterDetail)
                    {
                        group.Meta.Component = blockType.Display == BlockDisplayMode.Horizontal ?
                            "block-group-horizontal" : "block-group-vertical";
                    }

                    foreach (var prop in block.GetType().GetProperties(App.PropertyBindings))
                    {
                        if (typeof(Extend.IField).IsAssignableFrom(prop.PropertyType))
                        {
                            var fieldType = App.Fields.GetByType(prop.PropertyType);

                            group.Fields.Add(new FieldModel
                            {
                                Model = (Extend.IField)prop.GetValue(block),
                                Meta = new FieldMeta
                                {
                                    Id = prop.Name,
                                    Name = prop.Name,
                                    Component = fieldType.Component,
                                }
                            });
                        }
                    }

                    bool firstChild = true;
                    foreach (var child in ((Extend.BlockGroup)block).Items)
                    {
                        blockType = App.Blocks.GetByType(child.Type);

                        group.Items.Add(new BlockItemModel
                        {
                            IsActive = firstChild,
                            Model = child,
                            Meta = new BlockMeta
                            {
                                Name = blockType.Name,
                                Title = child.GetTitle(),
                                Icon = blockType.Icon,
                                Component = blockType.Component
                            }
                        });
                        firstChild = false;
                    }
                    model.Blocks.Add(group);
                }
                else
                {
                    model.Blocks.Add(new BlockItemModel
                    {
                        Model = block,
                        Meta = new BlockMeta
                        {
                            Name = blockType.Name,
                            Title = block.GetTitle(),
                            Icon = blockType.Icon,
                            Component = blockType.Component
                        }
                    });
                }
            }

            // Custom editors
            foreach (var editor in type.CustomEditors)
            {
                model.Editors.Add(new EditorModel
                {
                    Component = editor.Component,
                    Icon = editor.Icon,
                    Name = editor.Title
                });
            }
            return model;
        }

        private string GetState(PostBase post, bool isDraft)
        {
            if (post.Created != DateTime.MinValue)
            {
                if (post.Published.HasValue)
                {
                    if (isDraft)
                    {
                        return ContentState.Draft;
                    }
                    return ContentState.Published;
                }
                else
                {
                    return ContentState.Unpublished;
                }
            }
            return ContentState.New;
        }
    }
}