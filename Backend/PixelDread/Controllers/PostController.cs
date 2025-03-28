﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PixelDread.DTO;
using PixelDread.Models;
using PixelDread.Services;

namespace PixelDread.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PostController : ControllerBase
    {
        private readonly ApplicationContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public PostController(ApplicationContext context, UserManager<IdentityUser> userManager, IWebHostEnvironment env)
        {
            _context = context;
            _userManager = userManager;
            _env = env;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Post>>> GetPosts()
        {
            var posts = await _context.Posts
                .Include(p => p.Category)
                .Include(p => p.User)
                .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
                .Include(p => p.PostArticles).ThenInclude(pa => pa.Article)
                .Include(p => p.OGData).ThenInclude(og => og.FileInformations)
                .ToListAsync();

            return Ok(posts);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetPostById(int id)
        {
            var post = await _context.Posts
                .Include(p => p.PostArticles).ThenInclude(pa => pa.Article)
                .Include(p => p.User)
                .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
                .Include(p => p.OGData).ThenInclude(og => og.FileInformations)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (post == null)
            {
                return NoContent();
            }

            return Ok(post);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]

        public async Task<IActionResult> DeletePost(int id)
        {
            var post = await _context.Posts
                .Include(p => p.PostArticles).ThenInclude(pa => pa.Article)
                .Include(p => p.PostTags)
                .Include(p => p.OGData)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (post == null)
            {
                return NotFound();
            }

            // Smazání souborů mediálních článků (pokud existují)
            var fileInfosToDelete = new List<FileInformations>();
            foreach (var pa in post.PostArticles)
            {
                if (pa.Article is ArticleMedia mediaArticle)
                {
                    var fileInfo = await _context.FileInformations.FindAsync(mediaArticle.FileInformationsId);
                    if (fileInfo != null)
                    {
                        fileInfosToDelete.Add(fileInfo);
                    }
                }
            }

            // Odstranění příspěvku
            _context.Posts.Remove(post);
            await _context.SaveChangesAsync();

            // Odstranění fyzických souborů
            foreach (var fileInfo in fileInfosToDelete)
            {
                var filePath = Path.Combine(_env.WebRootPath, fileInfo.FilePath.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
                _context.FileInformations.Remove(fileInfo);
            }
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("by-category/{categoryId}")]
        public async Task<IActionResult> GetPostsByCategory(int categoryId)
        {
            var posts = await _context.Posts
                .Where(p => p.CategoryId == categoryId)
                .Include(p => p.Category)
                .Include(p => p.User)
                .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
                .Include(p => p.PostArticles).ThenInclude(pa => pa.Article)
                .Include(p => p.OGData).ThenInclude(og => og.FileInformations)
                .ToListAsync();

            if (!posts.Any())
            {
                return NoContent();
            }

            return Ok(posts);
        }
        [Authorize(Roles = "Admin")]


        [HttpPost("{postId}/tags")]
        public async Task<IActionResult> AddTagsToPost(int postId, [FromBody] List<int> tagIds)
        {
            var post = await _context.Posts
                .Include(p => p.PostTags)
                .FirstOrDefaultAsync(p => p.Id == postId);

            if (post == null)
            {
                return NotFound($"Příspěvek s ID {postId} nebyl nalezen.");
            }

            foreach (var tagId in tagIds)
            {
                var tag = await _context.Tags.FindAsync(tagId);
                if (tag == null)
                {
                    return BadRequest($"Tag s ID {tagId} nebyl nalezen.");
                }

                if (!post.PostTags.Any(pt => pt.TagId == tagId))
                {
                    post.PostTags.Add(new PostTag
                    {
                        PostId = postId,
                        TagId = tagId
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(post);
        }

        // Vytvoření příspěvku
        [Authorize(Roles = "Admin")]

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePost(int id, [FromForm] PostDto postDto)
        {
            // Ensure the post contains at least one article.
            if (postDto.Articles == null || postDto.Articles.Count == 0)
            {
                return BadRequest("Post must contain at least one article.");
            }

            // Load the existing post along with related entities.
            var post = await _context.Posts
                .Include(p => p.PostArticles).ThenInclude(pa => pa.Article)
                .Include(p => p.PostTags)
                .Include(p => p.OGData)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (post == null)
            {
                return NotFound();
            }

            // Update basic post properties.
            post.Name = postDto.Name;
            post.CategoryId = postDto.CategoryId;

            // Remove existing articles and their relationships.
            foreach (var pa in post.PostArticles.ToList())
            {
                _context.Articles.Remove(pa.Article);
                _context.PostArticles.Remove(pa);
            }

            // Create new articles based on the provided data.
            var newPostArticles = new List<PostArticle>();
            foreach (var articleDto in postDto.Articles)
            {
                Article article = null;
                switch (articleDto.Type)
                {
                    case ArticleType.Text:
                        article = new ArticleText
                        {
                            Content = articleDto.Content ?? ""
                        };
                        break;
                    case ArticleType.FAQ:
                        article = new ArticleFAQ
                        {
                            Question = articleDto.Question ?? "",
                            Answer = articleDto.Answer ?? ""
                        };
                        break;
                    case ArticleType.Link:
                        article = new ArticleLink
                        {
                            Url = articleDto.Url ?? "",
                            Placeholder = articleDto.Placeholder
                        };
                        break;
                    case ArticleType.Media:
                        var mediaArticle = new ArticleMedia
                        {
                            Description = articleDto.Description ?? "",
                            Alt = articleDto.Alt ?? ""
                        };
                        if (!articleDto.FileInformationsId.HasValue)
                        {
                            return BadRequest("FileId is required for media article.");
                        }
                        var file = await _context.FileInformations.FindAsync(articleDto.FileInformationsId.Value);
                        if (file == null)
                        {
                            return BadRequest("File not found.");
                        }
                        mediaArticle.FileInformationsId = file.Id;
                        article = mediaArticle;
                        break;
                    default:
                        continue;
                }
                if (article != null)
                {
                    article.PostId = post.Id;
                    _context.Articles.Add(article);
                    newPostArticles.Add(new PostArticle
                    {
                        PostId = post.Id,
                        Article = article,
                        ArticleType = articleDto.Type,
                        Order = articleDto.Order
                    });
                }
            }
            await _context.SaveChangesAsync();
            _context.PostArticles.AddRange(newPostArticles);

            // Update tags: remove existing and add new ones.
            _context.PostTags.RemoveRange(post.PostTags);
            if (postDto.TagIds != null && postDto.TagIds.Any())
            {
                foreach (var tagId in postDto.TagIds)
                {
                    var tag = await _context.Tags.FindAsync(tagId);
                    if (tag != null)
                    {
                        _context.PostTags.Add(new PostTag { PostId = post.Id, TagId = tagId });
                    }
                }
            }

            // Update OGData
            if (postDto.OGData != null)
            {
                if (post.OGData != null)
                {
                    // Update Title and Description only; do not update the slug.
                    post.OGData.Title = postDto.OGData.Title;
                    post.OGData.Description = postDto.OGData.Description;
                    // Slug remains unchanged.
                    if (postDto.OGData.FileInformationsId.HasValue)
                    {
                        var fileInfo = await _context.FileInformations.FindAsync(postDto.OGData.FileInformationsId.Value);
                        if (fileInfo == null)
                        {
                            return BadRequest("FileInformationsId does not exist.");
                        }
                        post.OGData.FileInformationsId = fileInfo.Id;
                    }
                }
                else
                {
                    // No OGData exists; create a new one including the provided slug.
                    var newOgData = new OGData
                    {
                        Title = postDto.OGData.Title,
                        Description = postDto.OGData.Description,
                        Slug = postDto.OGData.Slug,
                        PostId = post.Id
                    };
                    if (postDto.OGData.FileInformationsId.HasValue)
                    {
                        var fileInfo = await _context.FileInformations.FindAsync(postDto.OGData.FileInformationsId.Value);
                        if (fileInfo == null)
                        {
                            return BadRequest("FileInformationsId does not exist.");
                        }
                        newOgData.FileInformationsId = fileInfo.Id;
                    }
                    _context.OGDatas.Add(newOgData);
                    await _context.SaveChangesAsync();
                    post.OGData = newOgData;
                    post.OGDataId = newOgData.Id;
                }
            }

            await _context.SaveChangesAsync();
            return Ok(post);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]

        public async Task<IActionResult> CreatePost([FromForm] PostDto postDto)
        {
            if (postDto.Articles == null || postDto.Articles.Count == 0)
            {
                return BadRequest("Příspěvek musí obsahovat alespoň jeden článek.");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized("Uživatel musí být přihlášen.");
            }

            var post = new Post
            {
                Name = postDto.Name,
                CreatedAt = DateTime.UtcNow,
                Visibility = true,
                UserId = user.Id,
                User = user,
                CategoryId = postDto.CategoryId
            };

            _context.Posts.Add(post);
            await _context.SaveChangesAsync(); // Získáme post.Id

            var postArticles = new List<PostArticle>();
            foreach (var articleDto in postDto.Articles)
            {
                Article article = null;
                switch (articleDto.Type)
                {
                    case ArticleType.Text:
                        article = new ArticleText
                        {
                            Content = articleDto.Content ?? ""
                        };
                        break;
                    case ArticleType.FAQ:
                        article = new ArticleFAQ
                        {
                            Question = articleDto.Question ?? "",
                            Answer = articleDto.Answer ?? ""
                        };
                        break;
                    case ArticleType.Link:
                        article = new ArticleLink
                        {
                            Url = articleDto.Url ?? "",
                            Placeholder = articleDto.Placeholder
                        };
                        break;
                    case ArticleType.Media:
                        var mediaArticle = new ArticleMedia
                        {
                            Description = articleDto.Description ?? "",
                            Alt = articleDto.Alt ?? ""
                        };
                        if (!articleDto.FileInformationsId.HasValue)
                        {
                            return BadRequest("FileId is required for media article.");
                        }
                        var file = await _context.FileInformations.FindAsync(articleDto.FileInformationsId.Value);
                        if (file == null)
                        {
                            return BadRequest("File not found.");
                        }
                        mediaArticle.FileInformationsId = file.Id;
                        article = mediaArticle;
                        break;
                    default:
                        continue;
                }

                if (article != null)
                {
                    article.PostId = post.Id;
                    _context.Articles.Add(article);
                    postArticles.Add(new PostArticle
                    {
                        PostId = post.Id,
                        Article = article,
                        ArticleType = articleDto.Type,
                        Order = articleDto.Order
                    });
                }
            }
            await _context.SaveChangesAsync();
            _context.PostArticles.AddRange(postArticles);
            await _context.SaveChangesAsync();

            // Tagy
            if (postDto.TagIds != null && postDto.TagIds.Any())
            {
                foreach (var tagId in postDto.TagIds)
                {
                    var tag = await _context.Tags.FindAsync(tagId);
                    if (tag != null)
                    {
                        _context.PostTags.Add(new PostTag
                        {
                            PostId = post.Id,
                            TagId = tagId
                        });
                    }
                }
                await _context.SaveChangesAsync();
            }

            // OGData
            if (postDto.OGData != null)
            {
                var newOgData = new OGData
                {
                    Title = postDto.OGData.Title,
                    Description = postDto.OGData.Description,
                    Slug = postDto.OGData.Slug,
                    PostId = post.Id
                };

                // Pokud existuje FileInformationsId, zkontrolujeme, zda existuje i v DB
                if (postDto.OGData.FileInformationsId.HasValue)
                {
                    var fileInfo = await _context.FileInformations.FindAsync(postDto.OGData.FileInformationsId.Value);
                    if (fileInfo == null)
                    {
                        return BadRequest("FileInformationsId neexistuje v DB.");
                    }
                    newOgData.FileInformationsId = fileInfo.Id;
                }

                _context.OGDatas.Add(newOgData);
                await _context.SaveChangesAsync();

                post.OGDataId = newOgData.Id;
                post.OGData = newOgData;
                await _context.SaveChangesAsync();
            }

            return CreatedAtAction(nameof(GetPostById), new { id = post.Id }, post);
        }
        [HttpGet("slug/{slug}")]
        public async Task<IActionResult> GetPostBySlug(string slug)
        {
            var post = await _context.Posts
                .Include(p => p.OGData)
                    .ThenInclude(o => o.FileInformations)
                .Include(p => p.Category)
                .Include(p => p.User)
                .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
                .Include(p => p.PostArticles).ThenInclude(pa => pa.Article)
                .FirstOrDefaultAsync(p => p.OGData.Slug == slug);

            if (post == null)
            {
                return NotFound();
            }

            return Ok(post);
        }
        [HttpGet("slug-exists/{slug}")]
        public async Task<IActionResult> CheckSlugExists(string slug)
        {
            // Check if any post has an OGData with the given slug (case-insensitive)
            bool exists = await _context.Posts
                .AnyAsync(p => p.OGData != null && p.OGData.Slug.ToLower() == slug.ToLower());
            return Ok(exists);
        }
        [HttpPut("{id}/name")]
        [Authorize(Roles = "Admin")]

        public async Task<IActionResult> UpdatePostName(int id, [FromForm] string name)
        {
            var post = await _context.Posts.FindAsync(id);
            if (post == null)
            {
                return NotFound();
            }
            post.Name = name;
            await _context.SaveChangesAsync();
            return Ok(post);
        }
    }
}