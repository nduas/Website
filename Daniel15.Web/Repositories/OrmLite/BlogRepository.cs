﻿using System;
using System.Collections.Generic;
using Daniel15.Web.Models.Blog;
using ServiceStack.OrmLite;
using System.Linq;
using Daniel15.Web.Extensions;

namespace Daniel15.Web.Repositories.OrmLite
{
	/// <summary>
	/// Blog repository that uses OrmLite as the data access component
	/// </summary>
	public class BlogRepository : RepositoryBase<PostModel>, IBlogRepository
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="BlogRepository" /> class.
		/// </summary>
		/// <param name="connectionFactory">The database connection factory</param>
		public BlogRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
		{
		}

		/// <summary>
		/// Gets a post by slug.
		/// </summary>
		/// <param name="slug">The slug.</param>
		/// <returns>The post</returns>
		public PostModel GetBySlug(string slug)
		{
			var post = Connection.FirstOrDefault<PostModel>(x => x.Slug == slug);

			// Check if post wasn't found
			if (post == null)
				throw new ItemNotFoundException();

			// Get the main category as well
			// TODO: Do this using a join in the above query instead
			post.MainCategory = Connection.First<CategoryModel>(x => x.Id == post.MainCategoryId);

			return post;
		}

		/// <summary>
		/// Gets a post summary by slug.
		/// </summary>
		/// <param name="slug">The slug.</param>
		/// <returns>The post</returns>
		public PostSummaryModel GetSummaryBySlug(string slug)
		{
			var post = Connection.FirstOrDefault<PostSummaryModel>(x => x.Slug == slug);

			// Check if post wasn't found
			if (post == null)
				throw new ItemNotFoundException();

			return post;
		}

		/// <summary>
		/// Gets the categories for the specified blog post
		/// </summary>
		/// <param name="post">Blog post</param>
		/// <returns>Categories for this blog post</returns>
		public IList<CategoryModel> CategoriesForPost(PostSummaryModel post)
		{
			return Connection.Select<CategoryModel>(@"
				SELECT blog_categories.id, blog_categories.title, blog_categories.slug
				FROM blog_post_categories
				INNER JOIN blog_categories ON blog_categories.id = blog_post_categories.category_id
				WHERE blog_post_categories.post_id = {0}", post.Id);
		}

		/// <summary>
		/// Gets the latest blog posts
		/// </summary>
		/// <param name="count">Number of posts to return</param>
		/// <param name="offset">Post to start at</param>
		/// <returns>Latest blog posts</returns>
		public List<PostModel> LatestPosts(int count = 10, int offset = 0)
		{
			var posts = Connection.Select<PostModel>(query => query
				.Where(post => post.Published)
				.OrderByDescending(post => post.Date)
				.Limit(offset, count)
			);

			AddMainCategories(posts);
			return posts;
		}

		/// <summary>
		/// Gets the latest blog posts in this category
		/// </summary>
		/// <param name="category">Category to get posts from</param>
		/// <param name="count">Number of posts to return</param>
		/// <param name="offset">Post to start at</param>
		/// <returns>Latest blog posts</returns>
		public List<PostModel> LatestPosts(CategoryModel category, int count = 10, int offset = 0)
		{
			var posts = Connection.Select<PostModel>(@"
				SELECT id, title, slug, published, date, content, maincategory_id
				FROM blog_post_categories
				INNER JOIN blog_posts ON blog_posts.id = blog_post_categories.post_id
				WHERE blog_post_categories.category_id = {0}
					AND blog_posts.published = 1
				ORDER BY blog_posts.date DESC
				LIMIT {1}, {2}", category.Id, offset, count);
			
			AddMainCategories(posts);
			return posts;
		}

		/// <summary>
		/// Gets the latest blog posts for the specified year and month
		/// </summary>
		/// /// <param name="year">Year to get posts for</param>
		/// <param name="month">Month to get posts for</param>
		/// <param name="count">Number of posts to return</param>
		/// <param name="offset">Post to start at</param>
		/// <returns>Latest blog posts</returns>
		public List<PostModel> LatestPostsForMonth(int year, int month, int count = 10, int offset = 0)
		{
			var firstDate = new DateTime(year, month, day: 1);
			var lastDate = firstDate.AddMonths(1);

			var posts = Connection.Select<PostModel>(query => query
				.Where(post => 
					post.Published 
					&& post.UnixDate >= firstDate.ToUnix()
					&& post.UnixDate < lastDate.ToUnix()
				)
				.OrderByDescending(post => post.Date)
				.Limit(offset, count)
			);

			AddMainCategories(posts);
			return posts;
		}

		/// <summary>
		/// Loads all the main category information for the blog posts, and sets
		/// the MainCategory column on the posts
		/// TODO: Use a join for this!! Figure out how to do it with OrmLite
		/// </summary>
		/// <param name="posts"></param>
		private void AddMainCategories(IList<PostModel> posts)
		{
			var categoryIds = posts.Select(post => post.MainCategoryId).Distinct();
			if (!categoryIds.Any())
				return;

			//var categories = Connection.Select<CategoryModel>(cat => Sql.In(cat.Id, categoryIds)).ToDictionary(x => x.Id); // Sql.In() expects param array, didn't work
			var categories = Connection.Select<CategoryModel>("id IN (" + string.Join(", ", categoryIds) + ")").ToDictionary(x => x.Id);

			foreach (var post in posts)
				post.MainCategory = categories[post.MainCategoryId];
		}

		/// <summary>
		/// Gets a reduced DTO of the latest posts (essentially everything except content)
		/// </summary>
		/// <param name="count">Number of posts to return</param>
		/// <returns>Blog post summary</returns>
		public List<PostSummaryModel> LatestPostsSummary(int count = 10)
		{
			return Connection.Select<PostSummaryModel>(query => query
				.OrderByDescending(post => post.Date)
				.Limit(count)
			);
		}

		/// <summary>
		/// Gets the count of blog posts for every year and every month.
		/// </summary>
		/// <returns>A dictionary of years, which contains a dictionary of months and counts</returns>
		public IDictionary<int, IDictionary<int, int>> MonthCounts()
		{
			var counts = Connection.Select<MonthYearCount>(@"
SELECT MONTH(FROM_UNIXTIME(date)) AS month, YEAR(FROM_UNIXTIME(date)) AS year, COUNT(*) AS count
FROM blog_posts
GROUP BY year, month
ORDER BY year DESC, month DESC");

			IDictionary<int, IDictionary<int, int>> results = new Dictionary<int, IDictionary<int, int>>();

			foreach (var count in counts)
			{
				if (!results.ContainsKey(count.Year))
					results[count.Year] = new Dictionary<int, int>();

				results[count.Year][count.Month] = count.Count;
			}

			return results;
		}

		/// <summary>
		/// Gets a category by slug
		/// </summary>
		/// <param name="slug">Slug of the category</param>
		/// <returns>The category</returns>
		public CategoryModel GetCategory(string slug)
		{
			var category = Connection.FirstOrDefault<CategoryModel>(x => x.Slug == slug);

			// Check if category wasn't found
			if (category == null)
				throw new ItemNotFoundException();

			return category;
		}

		/// <summary>
		/// Get the total number of posts that are published
		/// </summary>
		/// <returns>Total number of posts</returns>
		public int PublishedCount()
		{
			return Connection.GetScalar<int>("SELECT COUNT(*) FROM blog_posts WHERE published = 1");
		}

		/// <summary>
		/// Get the total number of posts that are published in this category
		/// </summary>
		/// <returns>Total number of posts in the category</returns>
		public int PublishedCount(CategoryModel category)
		{
			return Connection.GetScalar<int>(@"
				SELECT COUNT(*) 
				FROM blog_post_categories 
				INNER JOIN blog_posts ON blog_posts.id = blog_post_categories.post_id
				WHERE blog_post_categories.category_id = {0}
					AND blog_posts.published = 1", category.Id);
		}

		/// <summary>
		/// Get the total number of posts that are published in this month and year
		/// </summary>
		/// <param name="year">Year to get count for</param>
		/// <param name="month">Month to get count for</param>
		/// <returns>Total number of posts that were posted in this month</returns>
		public int PublishedCountForMonth(int year, int month)
		{
			var firstDate = new DateTime(year, month, day: 1);
			var lastDate = firstDate.AddMonths(1);

			return Connection.GetScalar<int>(@"
				SELECT COUNT(*) 
				FROM blog_posts 
				WHERE published = 1
				AND date BETWEEN {0} AND {1}", firstDate.ToUnix(), lastDate.ToUnix());
		}

		private class MonthYearCount
		{
			public int Month { get; set; }
			public int Year { get; set; }
			public int Count { get; set; }
		}
	}
}